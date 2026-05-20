using System.IO;
 
var folder   = @"C:\temp\PBIRoleCopy";
var targetDb = Model.Database.Name;
 
if (!Directory.Exists(folder))
{
    Error("Folder not found: " + folder + "\n\nRun Export first.");
    return;
}
 
// file picker (default to latest)
var dlg = new System.Windows.Forms.OpenFileDialog();
dlg.InitialDirectory = folder;
dlg.Filter           = "Role export files|roles_*.json|All JSON files|*.json|All files|*.*";
dlg.Title            = "Select role export file to import into: " + targetDb;
dlg.RestoreDirectory = true;
 
var files = Directory.GetFiles(folder, "roles_*.json");
if (files.Length > 0)
{
    System.Array.Sort(files);
    dlg.FileName = System.IO.Path.GetFileName(files[files.Length - 1]);
}
 
if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) { Info("Cancelled."); return; }
 
var selectedFile = dlg.FileName;
var raw          = File.ReadAllText(selectedFile);
var json         = Newtonsoft.Json.Linq.JObject.Parse(raw);
 
// extract metadata
var sourceModel = "(unknown)"; var roleCount = "?";
var exportedAt  = "?";         var exportedBy = "?";
if (json["_meta"] != null) {
    if (json["_meta"]["sourceModel"] != null) sourceModel = json["_meta"]["sourceModel"].ToString();
    if (json["_meta"]["roleCount"]   != null) roleCount   = json["_meta"]["roleCount"].ToString();
    if (json["_meta"]["exportedAt"]  != null) exportedAt  = json["_meta"]["exportedAt"].ToString();
    if (json["_meta"]["exportedBy"]  != null) exportedBy  = json["_meta"]["exportedBy"].ToString();
}
 
// confirmation dialog
var msg = "─── IMPORT CONFIRMATION ───\n\n" +
          "FILE:     " + System.IO.Path.GetFileName(selectedFile) + "\n" +
          "SOURCE:   " + sourceModel + "\n" +
          "ROLES:    " + roleCount + "\n" +
          "EXPORTED: " + exportedAt + "\n" +
          "BY:       " + exportedBy + "\n\n        ↓ ↓ ↓\n\n" +
          "TARGET:   " + targetDb + "\n\n" +
          "WARNING: This will OVERWRITE existing roles.\n\nContinue?";
 
var ok = System.Windows.Forms.MessageBox.Show(msg, "Confirm Role Import",
    System.Windows.Forms.MessageBoxButtons.YesNo,
    System.Windows.Forms.MessageBoxIcon.Warning);
if (ok != System.Windows.Forms.DialogResult.Yes) { Info("Cancelled."); return; }
 
// same-model safety check
if (sourceModel == targetDb) {
    var ok2 = System.Windows.Forms.MessageBox.Show(
        "Source and target are the SAME model:\n\n" + targetDb + "\n\nReally continue?",
        "Same Model Detected",
        System.Windows.Forms.MessageBoxButtons.YesNo,
        System.Windows.Forms.MessageBoxIcon.Stop);
    if (ok2 != System.Windows.Forms.DialogResult.Yes) { Info("Cancelled."); return; }
}
 
// transform + execute
var tmsl = raw.Replace("##TARGETDB##", targetDb).Replace("\"default\"", "\"AzureAD\"");
var parsed = Newtonsoft.Json.Linq.JObject.Parse(tmsl);
if (parsed["_meta"] != null) parsed.Remove("_meta");
tmsl = parsed.ToString(Newtonsoft.Json.Formatting.None);
 
ExecuteCommand(tmsl);
 
// audit log
File.AppendAllText(System.IO.Path.Combine(folder, "audit.log"),
    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | IMPORT | " +
    sourceModel + " -> " + targetDb + " | " + roleCount + " roles | " +
    System.IO.Path.GetFileName(selectedFile) + "\r\n");
 
Info("Import complete!\n\n" + sourceModel + "\n  ->  " + targetDb +
     "\n\n" + roleCount + " roles imported.");
