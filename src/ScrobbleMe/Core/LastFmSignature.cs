// Підпис запитів до last.fm API (api_sig).
// Використовується LastFmClient для кожного методу, що вимагає автентифікації.
// Алгоритм: https://www.last.fm/api/authspec#_8-signing-calls
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ScrobbleMe.Core;

public static class LastFmSignature
{
    /// <summary>
    /// md5(усі параметри, відсортовані за назвою, у вигляді «назва+значення» підряд + секрет).
    /// format і callback у підпис не входять — так вимагає last.fm.
    /// </summary>
    public static string Sign(IEnumerable<KeyValuePair<string, string>> parameters, string secret)
    {
        var sb = new StringBuilder();
        foreach (var kv in parameters
                     .Where(p => p.Key != "format" && p.Key != "callback" && p.Key != "api_sig")
                     .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key).Append(kv.Value);
        }
        sb.Append(secret);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
