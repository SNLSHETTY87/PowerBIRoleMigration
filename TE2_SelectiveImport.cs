#r "System.Drawing"

using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

// ── colours ──────────────────────────────────────────────────────
var cBG       = Color.FromArgb(24, 24, 40);
var cPanel    = Color.FromArgb(32, 32, 52);
var cCard     = Color.FromArgb(40, 40, 64);
var cAccent   = Color.FromArgb(243, 111, 33);
var cBlue     = Color.FromArgb(15,  52,  96);
var cText     = Color.FromArgb(224, 224, 224);
var cMuted    = Color.FromArgb(120, 120, 140);
var cRed      = Color.FromArgb(198,  40,  40);
var cWhite    = Color.White;
var cNewBg    = Color.FromArgb(22,  44,  22);
var cUpdBg    = Color.FromArgb(44,  32,  14);
var cSelBg    = Color.FromArgb(50,  60, 100);
var cGreenTxt = Color.FromArgb(100, 220, 100);
var cOrgTxt   = Color.FromArgb(255, 170,  50);
var cGreen    = Color.FromArgb(46,  125,  50);

// ── fonts ─────────────────────────────────────────────────────────
var fTitle = new Font("Segoe UI", 13f, FontStyle.Bold);
var fBold  = new Font("Segoe UI",  9f, FontStyle.Bold);
var fBody  = new Font("Segoe UI",  9f, FontStyle.Regular);
var fMono  = new Font("Consolas",  8f, FontStyle.Regular);
var fSmall = new Font("Segoe UI",  8f, FontStyle.Regular);

// ── config ────────────────────────────────────────────────────────
var folder   = @"C:\temp\PBIRoleCopy";
var targetDb = Model.Database.Name;

// ── pre-flight ────────────────────────────────────────────────────
if (!Directory.Exists(folder)) {
    MessageBox.Show("Folder not found:\n" + folder + "\n\nRun Export first.",
        "Folder Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

// ── file picker ───────────────────────────────────────────────────
var dlg = new OpenFileDialog();
dlg.InitialDirectory = folder;
dlg.Filter           = "Role export files|roles_*.json|All JSON|*.json|All files|*.*";
dlg.Title            = "Select export file to import into:  " + targetDb;
dlg.RestoreDirectory = true;
var allFiles = Directory.GetFiles(folder, "roles_*.json");
if (allFiles.Length > 0) {
    System.Array.Sort(allFiles);
    dlg.FileName = System.IO.Path.GetFileName(allFiles[allFiles.Length - 1]);
}
if (dlg.ShowDialog() != DialogResult.OK) { Info("Cancelled."); return; }

var selectedFile = dlg.FileName;
var raw = File.ReadAllText(selectedFile);
Newtonsoft.Json.Linq.JObject fileJson;
try { fileJson = Newtonsoft.Json.Linq.JObject.Parse(raw); }
catch {
    MessageBox.Show("Could not parse:\n" + selectedFile,
        "Parse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

// ── metadata ─────────────────────────────────────────────────────
var sourceModel  = "(unknown)";
var roleCountStr = "?";
var exportedAt   = "?";
var exportedBy   = "?";
var exportType   = "full";
if (fileJson["_meta"] != null) {
    if (fileJson["_meta"]["sourceModel"] != null) sourceModel  = fileJson["_meta"]["sourceModel"].ToString();
    if (fileJson["_meta"]["roleCount"]   != null) roleCountStr = fileJson["_meta"]["roleCount"].ToString();
    if (fileJson["_meta"]["exportedAt"]  != null) exportedAt   = fileJson["_meta"]["exportedAt"].ToString();
    if (fileJson["_meta"]["exportedBy"]  != null) exportedBy   = fileJson["_meta"]["exportedBy"].ToString();
    if (fileJson["_meta"]["exportType"]  != null) exportType   = fileJson["_meta"]["exportType"].ToString();
}

// ── parse ops ────────────────────────────────────────────────────
var ops = fileJson["sequence"] != null
    ? fileJson["sequence"]["operations"] as Newtonsoft.Json.Linq.JArray
    : null;
if (ops == null || ops.Count == 0) {
    MessageBox.Show("No operations found in file.",
        "Empty File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return;
}

// ── diff: classify roles ──────────────────────────────────────────
var existingRoles = new HashSet<string>();
foreach (var r in Model.Roles) existingRoles.Add(r.Name);

var roleNames   = new List<string>();
var roleIsNew   = new Dictionary<string, bool>();
var roleMemCnt  = new Dictionary<string, int>();
var roleFltrCnt = new Dictionary<string, int>();
int newCount = 0, updCount = 0;

foreach (var op in ops) {
    if (op["createOrReplace"] == null) continue;
    var rd  = op["createOrReplace"]["role"];
    var rn  = (rd != null && rd["name"] != null) ? rd["name"].ToString() : "(unknown)";
    bool isN = !existingRoles.Contains(rn);
    int  mc  = (rd != null && rd["members"]          != null) ? ((Newtonsoft.Json.Linq.JArray)rd["members"]).Count          : 0;
    int  fc  = (rd != null && rd["tablePermissions"] != null) ? ((Newtonsoft.Json.Linq.JArray)rd["tablePermissions"]).Count : 0;
    roleNames.Add(rn);
    roleIsNew[rn]   = isN;
    roleMemCnt[rn]  = mc;
    roleFltrCnt[rn] = fc;
    if (isN) newCount++; else updCount++;
}

// sort: NEW first, then alpha
roleNames.Sort((a, b) => {
    bool aN = roleIsNew.ContainsKey(a) && roleIsNew[a];
    bool bN = roleIsNew.ContainsKey(b) && roleIsNew[b];
    if (aN != bN) return aN ? -1 : 1;
    return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
});

// ═════════════════════════════════════════════════════════════════
// FORM
// ═════════════════════════════════════════════════════════════════
var form = new Form {
    Text            = "Import Roles  ·  " + targetDb,
    Width           = 700,
    Height          = 660,
    BackColor       = cBG,
    StartPosition   = FormStartPosition.CenterScreen,
    FormBorderStyle = FormBorderStyle.FixedSingle,
    MaximizeBox     = false
};

// ── header ────────────────────────────────────────────────────────
var pH = new Panel { Left=0, Top=0, Width=700, Height=54, BackColor=cPanel };
pH.Controls.Add(new Label {
    Text="Import Roles", Left=16, Top=8, Width=300, Height=26,
    Font=fTitle, ForeColor=cAccent, BackColor=Color.Transparent
});
pH.Controls.Add(new Label {
    Text="Target: " + targetDb, Left=16, Top=34, Width=660, Height=16,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
});
form.Controls.Add(pH);

// ── file info ────────────────────────────────────────────────────
var pInfo = new Panel { Left=12, Top=62, Width=672, Height=68, BackColor=cCard };
pInfo.Controls.Add(new Label {
    Text = "FILE:    " + System.IO.Path.GetFileName(selectedFile),
    Left=12, Top=6, Width=648, Height=16, Font=fMono, ForeColor=cOrgTxt, BackColor=Color.Transparent
});
pInfo.Controls.Add(new Label {
    Text = "SOURCE:  " + sourceModel + "    BY: " + exportedBy,
    Left=12, Top=24, Width=648, Height=16, Font=fMono, ForeColor=cText, BackColor=Color.Transparent
});
pInfo.Controls.Add(new Label {
    Text = "ROLES:   " + roleCountStr + " in file  |  " + newCount + " NEW  |  " + updCount + " UPDATE  |  " + exportType.ToUpper(),
    Left=12, Top=42, Width=648, Height=16, Font=fMono, ForeColor=cMuted, BackColor=Color.Transparent
});
form.Controls.Add(pInfo);

// ── filter row ────────────────────────────────────────────────────
var pFilt = new Panel { Left=12, Top=138, Width=672, Height=32, BackColor=cBG };
Action<Button,Color,Color> MkBtn2 = (b, bg, fg) => {
    b.BackColor=bg; b.ForeColor=fg; b.FlatStyle=FlatStyle.Flat;
    b.FlatAppearance.BorderSize=0; b.Cursor=Cursors.Hand; b.Font=fSmall;
};
var txtFilt = new TextBox {
    Left=0, Top=4, Width=230, Height=24,
    BackColor=cBlue, ForeColor=cText, Font=fBody,
    BorderStyle=BorderStyle.FixedSingle
};
var btnAll  = new Button { Text="✓ All",    Left=238, Top=4, Width=60, Height=24 };
var btnNone = new Button { Text="✗ None",   Left=304, Top=4, Width=60, Height=24 };
var btnNew2 = new Button { Text="● New",    Left=370, Top=4, Width=78, Height=24 };
var btnUpd  = new Button { Text="● Update", Left=454, Top=4, Width=78, Height=24 };
MkBtn2(btnAll,  cBlue,  cText);
MkBtn2(btnNone, cBlue,  cText);
MkBtn2(btnNew2, Color.FromArgb(25, 60, 25), cGreenTxt);
MkBtn2(btnUpd,  Color.FromArgb(60, 40, 10), cOrgTxt);
pFilt.Controls.Add(txtFilt);
pFilt.Controls.Add(btnAll);
pFilt.Controls.Add(btnNone);
pFilt.Controls.Add(btnNew2);
pFilt.Controls.Add(btnUpd);
form.Controls.Add(pFilt);

// ── owner-drawn checkedlistbox ────────────────────────────────────
var clb = new CheckedListBox {
    Left=12, Top=178, Width=672, Height=340,
    BackColor=cCard, ForeColor=cText, Font=fMono,
    BorderStyle=BorderStyle.FixedSingle,
    CheckOnClick=true,
    DrawMode=DrawMode.OwnerDrawFixed,
    ItemHeight=22
};
foreach (var rn in roleNames) clb.Items.Add(rn, true);

// owner draw — colour by NEW vs UPDATE
clb.DrawItem += (s2, de) => {
    if (de.Index < 0 || de.Index >= clb.Items.Count) return;
    var rn      = clb.Items[de.Index].ToString();
    bool isN_   = roleIsNew.ContainsKey(rn) && roleIsNew[rn];
    bool isSel  = (de.State & DrawItemState.Selected) != 0;
    bool isChk  = clb.GetItemChecked(de.Index);

    // row background
    var bgCol = isSel ? cSelBg : (isN_ ? cNewBg : cUpdBg);
    de.Graphics.FillRectangle(new SolidBrush(bgCol), de.Bounds);

    // manual checkbox
    var chkRect = new Rectangle(de.Bounds.Left + 4, de.Bounds.Top + 4, 13, 13);
    de.Graphics.DrawRectangle(new Pen(Color.FromArgb(150, 150, 180), 1), chkRect);
    if (isChk) {
        de.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(80, 130, 200)), chkRect);
        de.Graphics.DrawString("✓", new Font("Segoe UI", 7f, FontStyle.Bold),
            new SolidBrush(Color.White), de.Bounds.Left + 4, de.Bounds.Top + 3);
    }

    // status badge
    var badge    = isN_ ? "[ NEW ]  " : "[UPDATE] ";
    var badgeClr = new SolidBrush(isN_ ? cGreenTxt : cOrgTxt);
    de.Graphics.DrawString(badge, fMono, badgeClr, de.Bounds.Left + 22, de.Bounds.Top + 3);

    // role name
    int mc = roleMemCnt.ContainsKey(rn)  ? roleMemCnt[rn]  : 0;
    int fc = roleFltrCnt.ContainsKey(rn) ? roleFltrCnt[rn] : 0;
    var detail = " (" + mc + " mem, " + fc + " filter" + (fc == 1 ? "" : "s") + ")";
    de.Graphics.DrawString(rn,    fMono,  new SolidBrush(cText),  de.Bounds.Left + 100, de.Bounds.Top + 3);
    de.Graphics.DrawString(detail,fSmall, new SolidBrush(cMuted), de.Bounds.Left + 100 + (int)de.Graphics.MeasureString(rn, fMono).Width + 2, de.Bounds.Top + 5);
};
form.Controls.Add(clb);

// ── status bar ────────────────────────────────────────────────────
var lblCount = new Label {
    Text = roleNames.Count + " of " + roleNames.Count + " selected  |  " + newCount + " new  |  " + updCount + " updates",
    Left=12, Top=526, Width=672, Height=18,
    Font=fSmall, ForeColor=cGreen, BackColor=Color.Transparent
};
form.Controls.Add(lblCount);

// ── dry run ───────────────────────────────────────────────────────
var chkDry = new CheckBox {
    Text="Dry Run — preview only, no changes committed",
    Left=12, Top=548, Width=400, Height=20,
    ForeColor=cMuted, BackColor=cBG, Font=fSmall, FlatStyle=FlatStyle.Flat
};
form.Controls.Add(chkDry);

// ── action row ────────────────────────────────────────────────────
var pAct = new Panel { Left=12, Top=572, Width=672, Height=44, BackColor=cBG };
var btnImport = new Button {
    Text="▶  Import Selected", Left=0, Top=4, Width=200, Height=36,
    BackColor=cAccent, ForeColor=cWhite, Font=fBold,
    FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand
};
btnImport.FlatAppearance.BorderSize=0;
var btnCancel = new Button {
    Text="Cancel", Left=208, Top=4, Width=100, Height=36,
    BackColor=cBlue, ForeColor=cText, Font=fBold,
    FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand
};
btnCancel.FlatAppearance.BorderSize=0;
var lblResult = new Label {
    Text="", Left=316, Top=12, Width=356, Height=22,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
};
pAct.Controls.Add(btnImport);
pAct.Controls.Add(btnCancel);
pAct.Controls.Add(lblResult);
form.Controls.Add(pAct);

// ── update count helper ───────────────────────────────────────────
System.Action refreshCount = () => {
    var sel = clb.CheckedItems.Count;
    var tot = clb.Items.Count;
    int sN = 0, sU = 0;
    foreach (var it in clb.CheckedItems) {
        var rn_ = it.ToString();
        if (roleIsNew.ContainsKey(rn_) && roleIsNew[rn_]) sN++; else sU++;
    }
    lblCount.Text      = sel + " of " + tot + " selected  |  " + sN + " new  |  " + sU + " updates";
    lblCount.ForeColor = sel > 0 ? cGreen : cMuted;
    btnImport.Enabled   = sel > 0;
    btnImport.BackColor = sel > 0 ? cAccent : cMuted;
};

clb.ItemCheck += (s2, e2) =>
    form.BeginInvoke((System.Action)(() => refreshCount()));

// ── filter + toggle ───────────────────────────────────────────────
var chkCache = new HashSet<string>();

System.Action<string> rebuildList = (q) => {
    chkCache.Clear();
    foreach (var it in clb.CheckedItems) chkCache.Add(it.ToString());
    clb.BeginUpdate(); clb.Items.Clear();
    foreach (var rn in roleNames)
        if (q == "" || rn.ToLower().Contains(q.ToLower()))
            clb.Items.Add(rn, chkCache.Contains(rn));
    clb.EndUpdate();
    refreshCount();
};

txtFilt.TextChanged += (s2, e2) => rebuildList(txtFilt.Text);
btnAll.Click  += (s2, e2) => { clb.BeginUpdate(); for (int i=0;i<clb.Items.Count;i++) clb.SetItemChecked(i,true);  clb.EndUpdate(); refreshCount(); };
btnNone.Click += (s2, e2) => { clb.BeginUpdate(); for (int i=0;i<clb.Items.Count;i++) clb.SetItemChecked(i,false); clb.EndUpdate(); refreshCount(); };
btnNew2.Click += (s2, e2) => {
    clb.BeginUpdate();
    for (int i=0; i<clb.Items.Count; i++) {
        var rn_ = clb.Items[i].ToString();
        clb.SetItemChecked(i, roleIsNew.ContainsKey(rn_) && roleIsNew[rn_]);
    }
    clb.EndUpdate(); refreshCount();
};
btnUpd.Click += (s2, e2) => {
    clb.BeginUpdate();
    for (int i=0; i<clb.Items.Count; i++) {
        var rn_ = clb.Items[i].ToString();
        clb.SetItemChecked(i, roleIsNew.ContainsKey(rn_) && !roleIsNew[rn_]);
    }
    clb.EndUpdate(); refreshCount();
};

// dry run toggle
chkDry.CheckedChanged += (s2, e2) => {
    btnImport.Text      = chkDry.Checked ? "🔍 Dry Run Preview" : "▶  Import Selected";
    btnImport.BackColor = chkDry.Checked ? Color.FromArgb(40, 80, 40) : cAccent;
};

btnCancel.Click += (s2, e2) => form.Close();

// ── import click ─────────────────────────────────────────────────
btnImport.Click += (s2, e2) => {
    var selRoles = new List<string>();
    foreach (var it in clb.CheckedItems) selRoles.Add(it.ToString());
    if (selRoles.Count == 0) return;

    bool isDry = chkDry.Checked;
    int sN2=0, sU2=0;
    foreach (var rn_ in selRoles)
        if (roleIsNew.ContainsKey(rn_) && roleIsNew[rn_]) sN2++; else sU2++;

    // confirmation dialog
    var confirmMsg =
        (isDry ? "─── DRY RUN PREVIEW ───\n\n" : "─── IMPORT CONFIRMATION ───\n\n") +
        "FILE:     " + System.IO.Path.GetFileName(selectedFile) + "\n" +
        "SOURCE:   " + sourceModel + "\n" +
        "BY:       " + exportedBy + "\n\n" +
        "        ↓ ↓ ↓\n\n" +
        "TARGET:   " + targetDb + "\n\n" +
        "SELECTED: " + selRoles.Count + " roles\n" +
        "  → " + sN2 + " will be ADDED (new)\n" +
        "  → " + sU2 + " will OVERWRITE existing\n\n" +
        (isDry ? "DRY RUN — no changes will be made.\n\nContinue?" : "Continue?");

    var dlgResult = MessageBox.Show(confirmMsg,
        isDry ? "Dry Run Preview" : "Confirm Import",
        MessageBoxButtons.YesNo,
        isDry ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    if (dlgResult != DialogResult.Yes) return;

    // same-model safety
    if (!isDry && sourceModel == targetDb) {
        var ok2 = MessageBox.Show(
            "Source and target are the SAME model:\n\n" + targetDb + "\n\nReally continue?",
            "Same Model Detected", MessageBoxButtons.YesNo, MessageBoxIcon.Stop);
        if (ok2 != DialogResult.Yes) return;
    }

    btnImport.Enabled = false;
    btnImport.Text    = isDry ? "Previewing..." : "Importing...";
    Application.DoEvents();

    try {
        if (isDry) {
            var dryLog = "DRY RUN RESULT — no changes made\n\n" +
                "Would import to: " + targetDb + "\n" +
                "Roles selected:  " + selRoles.Count + "\n\n" +
                "WOULD ADD (" + sN2 + " new roles):\n";
            foreach (var rn_ in selRoles)
                if (roleIsNew.ContainsKey(rn_) && roleIsNew[rn_])
                    dryLog += "  + " + rn_ + " (" + (roleMemCnt.ContainsKey(rn_) ? roleMemCnt[rn_] : 0) + " members)\n";
            dryLog += "\nWOULD UPDATE (" + sU2 + " existing roles):\n";
            foreach (var rn_ in selRoles)
                if (roleIsNew.ContainsKey(rn_) && !roleIsNew[rn_])
                    dryLog += "  ≈ " + rn_ + " (" + (roleMemCnt.ContainsKey(rn_) ? roleMemCnt[rn_] : 0) + " members)\n";

            MessageBox.Show(dryLog, "Dry Run Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            lblResult.ForeColor = cGreenTxt;
            lblResult.Text      = "✓ Dry run — no changes made";
        }
        else {
            // build subset TMSL from raw file (string replace then filter ops)
            var replaced = raw
                .Replace("##TARGETDB##", targetDb)
                .Replace("\"default\"",  "\"AzureAD\"");

            var fullParsed = Newtonsoft.Json.Linq.JObject.Parse(replaced);
            var allOps     = fullParsed["sequence"]["operations"] as Newtonsoft.Json.Linq.JArray;
            var selSet     = new HashSet<string>(selRoles);
            var subOps     = new Newtonsoft.Json.Linq.JArray();

            foreach (var op in allOps) {
                if (op["createOrReplace"] == null) continue;
                var rd_  = op["createOrReplace"]["role"];
                var rn_  = (rd_ != null && rd_["name"] != null) ? rd_["name"].ToString() : "";
                if (selSet.Contains(rn_)) subOps.Add(op);
            }

            var tmsl = new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("sequence", new Newtonsoft.Json.Linq.JObject(
                    new Newtonsoft.Json.Linq.JProperty("operations", subOps)
                ))
            ).ToString(Newtonsoft.Json.Formatting.Indented);

            ExecuteCommand(tmsl);

            // audit log
            File.AppendAllText(System.IO.Path.Combine(folder, "audit.log"),
                System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " | IMPORT | " + sourceModel + " -> " + targetDb +
                " | " + selRoles.Count + " roles (" + sN2 + " new, " + sU2 + " upd)" +
                " | " + System.IO.Path.GetFileName(selectedFile) + "\r\n");

            lblResult.ForeColor = cGreenTxt;
            lblResult.Text      = "✓ " + selRoles.Count + " roles imported";

            MessageBox.Show(
                "Import complete!\n\n" +
                sourceModel + "\n  →  " + targetDb + "\n\n" +
                sN2 + " new roles added\n" +
                sU2 + " existing roles updated\n\n" +
                "Verify: Service → Workspace → Model → Security",
                "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            form.Close();
        }
    } catch (System.Exception ex) {
        lblResult.ForeColor = cRed;
        lblResult.Text      = "✗ " + ex.Message.Substring(0, System.Math.Min(60, ex.Message.Length));
    }
    btnImport.Enabled   = clb.CheckedItems.Count > 0;
    btnImport.Text      = isDry ? "🔍 Dry Run Preview" : "▶  Import Selected";
};

form.ShowDialog();
