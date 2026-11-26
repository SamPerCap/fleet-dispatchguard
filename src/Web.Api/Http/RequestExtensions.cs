namespace DispatchGuard.API.Http;

internal static class RequestExtensions
{
    public static bool IsRead(this HttpRequest request) =>
        HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);
}
