using Flurl.Http;
using Flurl.Http.Content;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer
{
    internal static class MultipartExtensions
    {
        internal static Task<IFlurlResponse> PatchMultipartAsync(this IFlurlRequest request, Action<CapturedMultipartContent> buildContent, HttpCompletionOption httpCompletionOption = default, CancellationToken cancellationToken = default)
        {
            var cmc = new CapturedMultipartContent(request.Settings);
            buildContent(cmc);
            return request.SendAsync(new HttpMethod("PATCH"), cmc, httpCompletionOption, cancellationToken);
        }
    }
}