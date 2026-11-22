using Windows.Foundation.Metadata;

namespace Fort.ind_UWP
{
    internal static class ApiContracts
    {
        public static bool Has1809 { get; } = ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 7);
    }
}
