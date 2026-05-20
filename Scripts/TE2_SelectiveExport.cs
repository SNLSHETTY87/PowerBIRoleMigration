using System.IO;
 
// ────────── config ──────────
var folder      = @"C:\temp\PBIRoleCopy";
var sourceModel = Model.Database.Name;
var safeName    = System.Text.RegularExpressions.Regex.Replace(sourceModel, @"[^A-Za-z0-9]", "_");
var timestamp   = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
var fileName    = "roles_" + safeName + "_" + timestamp + ".json";
var filePath    = System.IO.Path.Combine(folder, fileName);
 
if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
 
// ────────── build TMSL operations ──────────
var ops = new Newtonsoft.Json.Linq.JArray();
 
foreach (var role in Model.Roles)
{
    var members = new Newtonsoft.Json.Linq.JArray();
    foreach (var mem in role.Members)
    {
        members.Add(new Newtonsoft.Json.Linq.JObject(
            new Newtonsoft.Json.Linq.JProperty("memberName",       mem.MemberName),
            new Newtonsoft.Json.Linq.JProperty("memberId",         mem.MemberID),
            new Newtonsoft.Json.Linq.JProperty("identityProvider", "AzureAD")
        ));
    }
 
    var tps = new Newtonsoft.Json.Linq.JArray();
    foreach (var tp in role.TablePermissions)
    {
        if (string.IsNullOrEmpty(tp.FilterExpression)) continue;
        tps.Add(new Newtonsoft.Json.Linq.JObject(
            new Newtonsoft.Json.Linq.JProperty("name",             tp.Table.Name),
            new Newtonsoft.Json.Linq.JProperty("filterExpression", tp.FilterExpression)
        ));
    }
 
    ops.Add(new Newtonsoft.Json.Linq.JObject(
        new Newtonsoft.Json.Linq.JProperty("createOrReplace", new Newtonsoft.Json.Linq.JObject(
            new Newtonsoft.Json.Linq.JProperty("object", new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("database", "##TARGETDB##"),
                new Newtonsoft.Json.Linq.JProperty("role",     role.Name)
            )),
            new Newtonsoft.Json.Linq.JProperty("role", new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("name",             role.Name),
                new Newtonsoft.Json.Linq.JProperty("modelPermission",  role.ModelPermission.ToString().ToLower()),
                new Newtonsoft.Json.Linq.JProperty("tablePermissions", tps),
                new Newtonsoft.Json.Linq.JProperty("members",          members)
            ))
        ))
    ));
}
 
// ────────── wrap with metadata envelope ──────────
var envelope = new Newtonsoft.Json.Linq.JObject(
    new Newtonsoft.Json.Linq.JProperty("_meta", new Newtonsoft.Json.Linq.JObject(
        new Newtonsoft.Json.Linq.JProperty("sourceModel", sourceModel),
        new Newtonsoft.Json.Linq.JProperty("exportedAt",  System.DateTime.Now.ToString("o")),
        new Newtonsoft.Json.Linq.JProperty("exportedBy",  System.Environment.UserName),
        new Newtonsoft.Json.Linq.JProperty("roleCount",   Model.Roles.Count())
    )),
    new Newtonsoft.Json.Linq.JProperty("sequence", new Newtonsoft.Json.Linq.JObject(
        new Newtonsoft.Json.Linq.JProperty("operations", ops)
    ))
);
 
File.WriteAllText(filePath, envelope.ToString(Newtonsoft.Json.Formatting.Indented));
 
// ────────── audit log ──────────
File.AppendAllText(System.IO.Path.Combine(folder, "audit.log"),
    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | EXPORT | " +
    sourceModel + " | " + Model.Roles.Count() + " roles | " + fileName + "\r\n");
 
Info("Export complete!\n\n" +
     "Source:  " + sourceModel + "\n" +
     "Roles:   " + Model.Roles.Count() + "\n" +
     "File:    " + fileName + "\n\n" +
     "Saved to:\n" + folder);
