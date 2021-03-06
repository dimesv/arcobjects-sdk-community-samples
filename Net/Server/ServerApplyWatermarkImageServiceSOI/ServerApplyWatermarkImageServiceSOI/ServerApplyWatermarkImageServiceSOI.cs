/*

   Copyright 2016 Esri

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

   See the License for the specific language governing permissions and
   limitations under the License.

*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Collections.Specialized;

using System.Runtime.InteropServices;

using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Server;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.SOESupport;
using ESRI.ArcGIS.SOESupport.SOI;
using System.Drawing;


//TODO: sign the project (project properties > signing tab > sign the assembly)
//      this is strongly suggested if the dll will be registered using regasm.exe <your>.dll /codebase


namespace ServerApplyWatermarkImageServiceSOI
{
    [ComVisible(true)]
    [Guid("ccf64e95-f8e9-4bdd-b3cf-9349a4361f25")]
    [ClassInterface(ClassInterfaceType.None)]
    [ServerObjectInterceptor("MapServer",
        Description = "SOI example that applies watermark to exported images",
        DisplayName = "DotNet Apply Watermark Sample SOI for Image Service",
        Properties = "")]
    public class ServerApplyWatermarkImageServiceSOI : IServerObjectExtension, IRESTRequestHandler, IWebRequestHandler, IRequestHandler2, IRequestHandler
    {
        private string _soiName;
        private IServerObjectHelper _soHelper;
        private ServerLogger _serverLog;
        private string _outputDirectory = string.Empty;
        private RestSOIHelper _restSOIHelper;


        public ServerApplyWatermarkImageServiceSOI()
        {
            _soiName = this.GetType().Name;
        }

        public void Init ( IServerObjectHelper pSOH )
        {
            try
            {
                _soHelper = pSOH;
                _serverLog = new ServerLogger();

                _restSOIHelper = new RestSOIHelper(pSOH);

                try
                {
                    //interop problem?
                    var se4 = _restSOIHelper.ServerEnvironment as IServerEnvironmentEx;
                    var dirInfos = se4.GetServerDirectoryInfos();
                    dirInfos.Reset();
                    object dirInfo = dirInfos.Next();
                    while (dirInfo != null)
                    {
                        var dinfo2 = dirInfo as IServerDirectoryInfo2;
                        if (null != dinfo2 && dinfo2.Type == esriServerDirectoryType.esriSDTypeOutput)
                        {
                            _outputDirectory = dinfo2.Path;
                            break;
                        }
                        dirInfo = dirInfos.Next();
                    }
                }
                catch (Exception ignore)
                {
                    _outputDirectory = string.Empty;
                }

                _outputDirectory = _outputDirectory.Trim();
                if (string.IsNullOrEmpty(_outputDirectory))
                {
                    _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".init()", 500, "OutputDirectory is empty or missing. Reset to default.");
                    _outputDirectory = "C:\\arcgisserver\\directories\\arcgisoutput";
                }

                _serverLog.LogMessage(ServerLogger.msgType.infoDetailed, _soiName + ".init()", 500, "OutputDirectory is " + _outputDirectory);
                _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Initialized " + _soiName + " SOI.");
            }
            catch (Exception e)
            {
                _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".HandleRESTRequest()", 500, "Exception " + e.GetType().Name + " " + e.Message + " " + e.StackTrace);
                throw;
            }
        }

        public void Shutdown ()
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Shutting down " + _soiName + " SOI.");
        }

        #region REST interceptors

        public string GetSchema ()
        {
            try
            {
                IRESTRequestHandler restRequestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRESTRequestHandler>();
                if (restRequestHandler == null)
                    throw new RestErrorException("Service handler not found");

                return restRequestHandler.GetSchema();
            }
            catch (Exception e)
            {
                _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".HandleRESTRequest()", 500, "Exception " + e.GetType().Name + " " + e.Message + " " + e.StackTrace);
                throw;
            }
        }

        public byte[] HandleRESTRequest ( string Capabilities, string resourceName, string operationName,
            string operationInput, string outputFormat, string requestProperties, out string responseProperties )
        {
            try
            {
                responseProperties = null;
                _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleRESTRequest()",
                    200, "Request received in Sample Object Interceptor for handleRESTRequest");

                /*
                * Add code to manipulate REST requests here
                */

                // Find the correct delegate to forward the request too
                IRESTRequestHandler restRequestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRESTRequestHandler>();
                if (restRequestHandler == null)
                {
                    throw new RestErrorException("Service handler not found");
                }

                var response = restRequestHandler.HandleRESTRequest(
                        Capabilities, resourceName, operationName, operationInput,
                        outputFormat, requestProperties, out responseProperties);

                /*
                 * Manipulate the response.
                 * 
                 * Add watermark
                 */

                if (operationName.Equals("exportImage", StringComparison.CurrentCultureIgnoreCase))
                {
                    Image sourceImage = null;
                    if (outputFormat.Equals("image", StringComparison.CurrentCultureIgnoreCase))
                    {
                        sourceImage = Image.FromStream(new System.IO.MemoryStream(response));

                        var watermarker = new ApplyWatermark.ApplyWatermark();

                        var watermarkedImage = watermarker.Mark(sourceImage, "(c) ESRI Inc.");
                        var newResponse = new System.IO.MemoryStream();
                        watermarkedImage.Save(newResponse, sourceImage.RawFormat);

                        return newResponse.GetBuffer();
                    }
                    else if (outputFormat.Equals("json", StringComparison.CurrentCultureIgnoreCase))
                    {

                        var responseString = System.Text.Encoding.UTF8.GetString(response);
                        var jo = new JsonObject(responseString);
                        string hrefString = null;
                        if (!jo.TryGetString("href", out hrefString))
                            throw new RestErrorException("Export operation returned invalid response");

                        if (string.IsNullOrEmpty(hrefString))
                            throw new RestErrorException("Export operation returned invalid response");

                        // Generate output file location
                        var outputImageFileLocation = GetOutputImageFileLocation(hrefString);
                        
                        // debug logging
                        //_serverLog.LogMessage(ServerLogger.msgType.error, "debug", 0, "output is " + outputImageFileLocation);

                        var watermarker = new ApplyWatermark.ApplyWatermark();
                        Image watermarkedImage;

                        System.Drawing.Imaging.ImageFormat sourceImageFormat;
                        using( sourceImage = Image.FromFile(outputImageFileLocation))
                        {
                            sourceImageFormat = sourceImage.RawFormat;
                            watermarkedImage = watermarker.Mark(sourceImage, "(c) ESRI Inc.");
                        }
                        // make sure we dispose sourceImage handles before saving to it

                        watermarkedImage.Save(outputImageFileLocation, sourceImageFormat);
                        watermarkedImage.Dispose();

                        // return response as is because we have modified the file its pointing to
                        return response;
                    }
                    else if (outputFormat.Equals("kmz", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Note: Watermark can be added for the kmz format too. In this example we didn't
                        // implement it.
                        throw new RestErrorException("Kmz format is not supported");
                    }
                    else
                    {
                        throw new RestErrorException("Invalid operation parameters");
                    }
                }//if operationName==export
                return response;
            }
            catch (RestErrorException restException)
            {
                responseProperties = "{\"Content-Type\":\"text/plain;charset=utf-8\"}";
                return System.Text.Encoding.UTF8.GetBytes(restException.Message);
            }
            catch (Exception e)
            {
                _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".HandleRESTRequest()", 500, "Exception " + e.GetType().Name + " " + e.Message + " " + e.StackTrace);
                throw;
            }
        }

        #endregion

        #region SOAP interceptors

        public byte[] HandleStringWebRequest ( esriHttpMethod httpMethod, string requestURL,
            string queryString, string Capabilities, string requestData,
            out string responseContentType, out esriWebResponseDataType respDataType )
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleStringWebRequest()",
                200, "Request received in Sample Object Interceptor for HandleStringWebRequest");

            /*
             * Add code to manipulate requests here
             */

            IWebRequestHandler webRequestHandler = _restSOIHelper.FindRequestHandlerDelegate<IWebRequestHandler>();
            if (webRequestHandler != null)
            {
                return webRequestHandler.HandleStringWebRequest(
                        httpMethod, requestURL, queryString, Capabilities, requestData, out responseContentType, out respDataType);
            }

            responseContentType = null;
            respDataType = esriWebResponseDataType.esriWRDTPayload;
            //Insert error response here.
            return null;
        }

        public byte[] HandleBinaryRequest ( ref byte[] request )
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleBinaryRequest()",
                  200, "Request received in Sample Object Interceptor for HandleBinaryRequest");

            /*
             * Add code to manipulate requests here
             */

            IRequestHandler requestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRequestHandler>();
            if (requestHandler != null)
            {
                return requestHandler.HandleBinaryRequest(request);
            }

            //Insert error response here.
            return null;
        }

        public byte[] HandleBinaryRequest2 ( string Capabilities, ref byte[] request )
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleBinaryRequest2()",
                  200, "Request received in Sample Object Interceptor for HandleBinaryRequest2");

            /*
             * Add code to manipulate requests here
             */

            IRequestHandler2 requestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRequestHandler2>();
            if (requestHandler != null)
            {
                return requestHandler.HandleBinaryRequest2(Capabilities, request);
            }

            //Insert error response here.
            return null;
        }

        public string HandleStringRequest ( string Capabilities, string request )
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleStringRequest()",
                   200, "Request received in Sample Object Interceptor for HandleStringRequest");

            /*
             * Add code to manipulate requests here
             */

            IRequestHandler requestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRequestHandler>();
            if (requestHandler != null)
            {
                return requestHandler.HandleStringRequest(Capabilities, request);
            }

            //Insert error response here.
            return null;
        }

        #endregion

        #region Utility code


        /**
   * Generate physical file path from virtual path
   * 
   * @param virtualPath Path returned by the MapServer SO
   * @return
   */
        private String GetOutputImageFileLocation ( String virtualPath )
        {
            /*
             * Sample output returned by MapServer SO
             * 
             * example : /rest/directories/arcgisoutput/SampleWorldCities_MapServer/
             * _ags_map26c62f8c2c0c4965b53e87e300e1912f.png
             */
            var virtualPathParts = virtualPath.Split('/');
            String imageFileLocation = _outputDirectory;

            // build the physical path to the image file
            bool buildPath = false;
            foreach (String virtualPathPart in virtualPathParts)
            {
                if (buildPath)
                {
                    imageFileLocation += "\\" + virtualPathPart;
                }
                if (virtualPathPart.Equals("arcgisoutput", StringComparison.CurrentCultureIgnoreCase))
                {
                    buildPath = true;
                }
            }

            return imageFileLocation;
        }
        #endregion
    }
}
