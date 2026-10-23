using System.Text;
using UnityEngine;

public static class NameplateDisplayFormatter
{
    public static string FormatDisplayName(string username, bool isHost)
    {
        return isHost ? "[HOST] " + username : username;
    }

    public static Color GetDisplayColor(bool isHost)
    {
        return isHost ? new Color(1f, 0.28f, 0.28f) : Color.white;
    }

    public static float ComputeAlpha(float distance, float fadeStartDistance, float fadeEndDistance)
    {
        float fadeProgress = Mathf.InverseLerp(fadeStartDistance, fadeEndDistance, distance);
        return 1f - fadeProgress;
    }

    public static string TruncateToUtf8ByteLimit(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        byte[] encodedBytes = Encoding.UTF8.GetBytes(value);
        byte[] truncated = new byte[maxBytes];
        System.Array.Copy(encodedBytes, truncated, maxBytes);
        return Encoding.UTF8.GetString(truncated).TrimEnd('�');
    }
}
