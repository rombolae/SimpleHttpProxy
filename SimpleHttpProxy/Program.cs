﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace SimpleHttpProxy
{
    public class Program
    {
        private static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            var listener = new HttpListener();
            listener.Prefixes.Add("http://*:7777/");
            listener.Start();
            Console.WriteLine("Listening...");
            while (true)
            {
                var ctx = listener.GetContext();
                new Thread(new Relay(ctx).ProcessRequest).Start();
            }
        }
    }

    public class Relay
    {
        private readonly HttpListenerContext _originalContext;

        public Relay(HttpListenerContext originalContext)
        {
            _originalContext = originalContext;
        }

        public void ProcessRequest()
        {
            var rawUrl = "https://postman-echo.com" + _originalContext.Request.Url.PathAndQuery;//originalContext.Request.RawUrl;
            ConsoleUtilities.WriteRequest("Proxy receive a request for: " + rawUrl);

            var relayRequest = (HttpWebRequest) WebRequest.Create(rawUrl);
            relayRequest.Method = _originalContext.Request.HttpMethod;
            //foreach (var k in _originalContext.Request.Headers.AllKeys)
            //{
            //    try
            //    {
            //        relayRequest.Headers.Add(k, _originalContext.Request.Headers[k]);
            //    }
            //    catch(Exception){}
            //}
            relayRequest.KeepAlive = false;
            relayRequest.Proxy.Credentials = CredentialCache.DefaultCredentials;
            relayRequest.UserAgent = _originalContext.Request.UserAgent;
           
            var requestData = new RequestState(relayRequest, _originalContext);
            relayRequest.BeginGetResponse(ResponseCallBack, requestData);
        }

        private static void ResponseCallBack(IAsyncResult asynchronousResult)
        {
            RequestState requestData = null;
            try
            {
                requestData = (RequestState)asynchronousResult.AsyncState;
                ConsoleUtilities.WriteResponse("Proxy receive a response from " + requestData.context.Request.RawUrl);

                using (var responseFromWebSiteBeingRelayed =
                    (HttpWebResponse)requestData.webRequest.EndGetResponse(asynchronousResult))
                {
                    using (var responseStreamFromWebSiteBeingRelayed =
                        responseFromWebSiteBeingRelayed.GetResponseStream())
                    {
                        var originalResponse = requestData.context.Response;

                        if (responseFromWebSiteBeingRelayed.ContentType.Contains("text/html"))
                        {
                            var reader = new StreamReader(responseStreamFromWebSiteBeingRelayed);
                            string html = reader.ReadToEnd();
                            //Here can modify html
                            byte[] byteArray = System.Text.Encoding.Default.GetBytes(html);
                            var stream = new MemoryStream(byteArray);
                            stream.CopyTo(originalResponse.OutputStream);
                        }
                        else
                        {
                            responseStreamFromWebSiteBeingRelayed.CopyTo(originalResponse.OutputStream);
                        }

                        originalResponse.OutputStream.Close();
                    }
                }
            }
            catch (WebException e)
            {
                var res = requestData?.context.Response;
                using (var response = e.Response)
                {
                    var httpResponse = (HttpWebResponse)response;
                    Console.WriteLine("Error code: {0}", httpResponse.StatusCode);
                    using (var data = response.GetResponseStream())
                    using (var reader = new StreamReader(data))
                    {
                        var text = reader.ReadToEnd();
                        var textBytes = Encoding.ASCII.GetBytes(text);
                        res?.OutputStream.Write(textBytes, 0, textBytes.Length);
                        Console.WriteLine(text);
                    }

                    foreach (var h in response.Headers.AllKeys)
                    {
                        res?.AddHeader(h, response.Headers[h]);
                    }

                    res.StatusCode = (int)httpResponse.StatusCode;
                    res.StatusDescription = httpResponse.StatusDescription;
                    res.ContentType = httpResponse.ContentType;
                }
                res.Close();
            }
        }
    }

    public static class ConsoleUtilities
    {
        public static void WriteRequest(string info)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(info);
            Console.ResetColor();
        }
        public static void WriteResponse(string info)
        {
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(info);
            Console.ResetColor();
        }
    }

    public class RequestState
    {
        public readonly HttpWebRequest webRequest;
        public readonly HttpListenerContext context;

        public RequestState(HttpWebRequest request, HttpListenerContext context)
        {
            webRequest = request;
            this.context = context;
        }
    }

}