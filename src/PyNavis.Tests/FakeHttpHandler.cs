using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PyNavis.Tests
{
    /// <summary>Answers every request with one canned response and remembers what
    /// was sent, so provider tests never touch the network.</summary>
    public sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _contentType;

        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public FakeHttpHandler(HttpStatusCode status, string body, string contentType = "text/event-stream")
        {
            _status = status;
            _body = body;
            _contentType = contentType;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _contentType),
                RequestMessage = request,
            };
            return response;
        }
    }
}
