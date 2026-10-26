public static class ApiConfig
{
    private static string overrideUrl;

    public static string BASE_URL
    {
        get
        {
            if (!string.IsNullOrEmpty(overrideUrl)) return overrideUrl;
            return "http://localhost:8000";
        }
    }

    public static void SetBaseUrl(string url)
    {
        overrideUrl = url;
    }
}

