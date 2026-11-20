namespace DDReview.Core.Security;

public static class GoogleChallenge
{
    /// <summary>
    /// Adds <c>prompt=select_account</c> (Google's account chooser) to the sign-in address unless a prompt is already there.
    /// The Setup "Verify with Google" step sends <c>prompt=login</c> itself; Google must not get the parameter twice.
    /// </summary>
    public static string WithDefaultPrompt(string url, string prompt = "select_account")
    {
        var q = url.IndexOf('?');
        if (q >= 0)
        {
            var hash = url.IndexOf('#', q);
            var query = hash >= 0 ? url[(q + 1)..hash] : url[(q + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                if (pair.Equals("prompt", StringComparison.OrdinalIgnoreCase) || pair.StartsWith("prompt=", StringComparison.OrdinalIgnoreCase))
                    return url;
        }
        return url + (q >= 0 ? "&" : "?") + "prompt=" + Uri.EscapeDataString(prompt);
    }
}
