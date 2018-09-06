using System;

namespace AdaFruit.IO
{
   public partial class Client
   {
      public string ApiKey { set; get; }

      partial void PrepareRequest(System.Net.Http.HttpClient client, System.Net.Http.HttpRequestMessage request, string url)
      {
         client.DefaultRequestHeaders.Add("X-AIO-Key", ApiKey );
      }
   }
}
