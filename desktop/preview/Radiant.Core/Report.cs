using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Net;
using System.Text;

namespace Radiant.Core;

public static class Report
{
    public static string Serialize(string product, string version, HardwareSnapshot snapshot)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(new
        {
            product, version, readOnly = true, snapshot, checks = Diagnostics.Evaluate(snapshot),
        }, options);
    }

    public static string Html(string product, string version, HardwareSnapshot snapshot)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "Unavailable");
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <style>
            *{box-sizing:border-box}body{margin:0;background:#101c2b;color:#e8edf5;font:15px/1.7 'Segoe UI',sans-serif}
            main{max-width:920px;padding:64px 24px;margin:auto}header{border-bottom:1px solid #35455a;padding-bottom:32px}
            h1{font-size:42px;letter-spacing:-1px;line-height:1.1;margin:12px 0}h2{font-size:24px;margin:40px 0 20px}
            h3{font-size:18px;margin:0 0 16px}p{margin:8px 0;color:#b9c6d8}.eyebrow{color:#a4c8f4;font-size:12px;letter-spacing:2px}
            section{background:#172638;padding:24px;margin:16px 0;border:1px solid #35455a;border-radius:6px}
            table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:10px 0;vertical-align:top;border-bottom:1px solid #35455a;overflow-wrap:anywhere}
            th{width:34%;padding-right:18px;font-weight:500;color:#b9c6d8}.status{font-size:12px;color:#a4c8f4}
            .attention{color:#efc275}.unknown{color:#b9c6d8}.pass{color:#93cfb7}.next{padding-top:12px;border-top:1px solid #35455a;margin-top:16px}
            footer{font-size:12px;margin-top:40px;color:#b9c6d8}@media(max-width:500px){main{padding:32px 16px}h1{font-size:32px}section{padding:18px}}
            @media print{body{background:white;color:#172638}main{padding:0}section{background:white;break-inside:avoid;border-color:#ddd}p,th,footer,.status{color:#333}h2{break-after:avoid}}
            </style><title>
            """);
        html.Append(E(product)).Append(" · Hardware report</title></head><body><main><header><p class=\"eyebrow\">LOCAL HARDWARE REPORT</p><h1>")
            .Append(E(product)).Append("</h1><p>").Append(E(version)).Append(" · Read-only snapshot</p><p>Collected ")
            .Append(E(snapshot.CollectedAt.ToString("MMM d, yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture)))
            .Append("</p><p>A point-in-time reading of this PC. No Windows settings were changed.</p></header><h2>Hardware details</h2>");
        foreach (var group in HardwareDetails.Create(snapshot))
        {
            html.Append("<section><h3>").Append(E(group.Title)).Append("</h3><table>");
            foreach (var row in group.Rows)
                html.Append("<tr><th scope=\"row\">").Append(E(row.Label)).Append("</th><td>").Append(E(row.Value)).Append("</td></tr>");
            html.Append("</table></section>");
        }
        html.Append("<h2>System checks</h2>");
        foreach (var check in Diagnostics.Evaluate(snapshot))
        {
            html.Append("<section><span class=\"status ").Append(check.Status.ToString().ToLowerInvariant()).Append("\">")
                .Append(E(check.Status.ToString())).Append("</span><h3>").Append(E(check.Title)).Append("</h3><p>")
                .Append(E(check.Evidence)).Append("</p><p>").Append(E(check.Explanation))
                .Append("</p><p class=\"next\"><strong>Next step</strong><br>").Append(E(check.NextStep)).Append("</p></section>");
        }
        return html.Append("<footer>This file stays where you save it. It contains hardware readings, not serial numbers, usernames or network addresses. Unknown means the reading was unavailable, not a failed check. Readings are not a benchmark or a guarantee of performance.</footer></main></body></html>").ToString();
    }
}
