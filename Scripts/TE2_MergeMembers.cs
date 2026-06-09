#r "System.Drawing"

using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

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
var cMatchBg  = Color.FromArgb(22,  44,  22);  // green tint — has new members
var cSyncBg   = Color.FromArgb(30,  32,  44);  // neutral — in sync
var cMissBg   = Color.FromArgb(44,  22,  22);  // red tint — not in target
var cSelBg    = Color.FromArgb(50,  60, 100);
var cGreenTxt = Color.FromArgb(100, 220, 100);
var cYellowTxt= Color.FromArgb(255, 213,  79);
var cRedTxt   = Color.FromArgb(239, 154, 154);
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
dlg.Title            = "Select source export file — merge members into: " + targetDb;
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
    MessageBox.Show("Could not parse file:\n" + selectedFile,
        "Parse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

// ── metadata ─────────────────────────────────────────────────────
var sourceModel  = "(unknown)";
var exportedBy   = "?";
var exportedAt   = "?";
if (fileJson["_meta"] != null) {
    if (fileJson["_meta"]["sourceModel"] != null) sourceModel  = fileJson["_meta"]["sourceModel"].ToString();
    if (fileJson["_meta"]["exportedBy"]  != null) exportedBy   = fileJson["_meta"]["exportedBy"].ToString();
    if (fileJson["_meta"]["exportedAt"]  != null) exportedAt   = fileJson["_meta"]["exportedAt"].ToString();
}

// ── parse operations ─────────────────────────────────────────────
var ops = fileJson["sequence"] != null
    ? fileJson["sequence"]["operations"] as Newtonsoft.Json.Linq.JArray
    : null;
if (ops == null || ops.Count == 0) {
    MessageBox.Show("No operations found.", "Empty File",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return;
}

// ── build target snapshot ────────────────────────────────────────
// For each target role: name → set of memberIds (for fast dedup check)
var targetRoleMembers = new Dictionary<string, HashSet<string>>();
foreach (var r in Model.Roles) {
    var ids = new HashSet<string>();
    foreach (var m in r.Members) {
        if (!string.IsNullOrEmpty(m.MemberID)) ids.Add(m.MemberID);
    }
    targetRoleMembers[r.Name] = ids;
}

// ── classify each source role ────────────────────────────────────
//   MATCH   — exists in target AND has new members to add
//   SYNC    — exists in target, all source members already present
//   MISSING — does not exist in target (skip with warning)

var roleNames    = new List<string>();
var roleStatus   = new Dictionary<string, string>();   // "MATCH" | "SYNC" | "MISSING"
var roleSourceMembers = new Dictionary<string, Newtonsoft.Json.Linq.JArray>(); // members from file
var roleNewMemCnt   = new Dictionary<string, int>();   // count of NEW members to add
var roleSourceMemCnt = new Dictionary<string, int>();  // total source members
var roleTargetMemCnt = new Dictionary<string, int>();  // current target members

int matchCount = 0, syncCount = 0, missingCount = 0;

foreach (var op in ops) {
    if (op["createOrReplace"] == null) continue;
    var rd = op["createOrReplace"]["role"];
    if (rd == null || rd["name"] == null) continue;
    var rn = rd["name"].ToString();
    var srcMembers = rd["members"] as Newtonsoft.Json.Linq.JArray;
    if (srcMembers == null) srcMembers = new Newtonsoft.Json.Linq.JArray();

    roleNames.Add(rn);
    roleSourceMembers[rn] = srcMembers;
    roleSourceMemCnt[rn]  = srcMembers.Count;

    if (!targetRoleMembers.ContainsKey(rn)) {
        roleStatus[rn] = "MISSING";
        roleNewMemCnt[rn]    = 0;
        roleTargetMemCnt[rn] = 0;
        missingCount++;
    } else {
        var targetIds = targetRoleMembers[rn];
        roleTargetMemCnt[rn] = targetIds.Count;

        // count new members in source not yet in target
        int newCount = 0;
        foreach (var sm in srcMembers) {
            var smId = sm["memberId"] != null ? sm["memberId"].ToString() : "";
            if (!string.IsNullOrEmpty(smId) && !targetIds.Contains(smId)) newCount++;
        }
        roleNewMemCnt[rn] = newCount;

        if (newCount > 0) { roleStatus[rn] = "MATCH"; matchCount++; }
        else              { roleStatus[rn] = "SYNC";  syncCount++; }
    }
}

// sort: MATCH first, SYNC, MISSING
roleNames.Sort((a, b) => {
    var sa = roleStatus[a]; var sb = roleStatus[b];
    int oa = sa == "MATCH" ? 0 : sa == "SYNC" ? 1 : 2;
    int ob = sb == "MATCH" ? 0 : sb == "SYNC" ? 1 : 2;
    if (oa != ob) return oa.CompareTo(ob);
    return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
});

// ═════════════════════════════════════════════════════════════════
// FORM
// ═════════════════════════════════════════════════════════════════
var form = new Form {
    Text            = "Merge Members  ·  " + targetDb,
    Width           = 720,
    Height          = 680,
    BackColor       = cBG,
    StartPosition   = FormStartPosition.CenterScreen,
    FormBorderStyle = FormBorderStyle.FixedSingle,
    MaximizeBox     = false
};

// ── header ────────────────────────────────────────────────────────
var pH = new Panel { Left=0, Top=0, Width=720, Height=60, BackColor=cPanel };
pH.Controls.Add(new Label {
    Text="Merge Role Members  ·  Members-Only Mode",
    Left=16, Top=8, Width=500, Height=26,
    Font=fTitle, ForeColor=cAccent, BackColor=Color.Transparent
});
pH.Controls.Add(new Label {
    Text="Target: " + targetDb + "   (RLS DAX filters will not be modified)",
    Left=16, Top=36, Width=680, Height=18,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
});
form.Controls.Add(pH);

// ── file info ────────────────────────────────────────────────────
var pInfo = new Panel { Left=12, Top=68, Width=692, Height=84, BackColor=cCard };
pInfo.Controls.Add(new Label {
    Text="FILE:    " + System.IO.Path.GetFileName(selectedFile),
    Left=12, Top=6, Width=668, Height=16,
    Font=fMono, ForeColor=cYellowTxt, BackColor=Color.Transparent
});
pInfo.Controls.Add(new Label {
    Text="SOURCE:  " + sourceModel + "   BY: " + exportedBy,
    Left=12, Top=24, Width=668, Height=16,
    Font=fMono, ForeColor=cText, BackColor=Color.Transparent
});
pInfo.Controls.Add(new Label {
    Text="ROLES:   " + roleNames.Count + " total  |  " + matchCount + " NEW MEMBERS  |  " + syncCount + " IN SYNC  |  " + missingCount + " MISSING",
    Left=12, Top=42, Width=668, Height=16,
    Font=fMono, ForeColor=cMuted, BackColor=Color.Transparent
});
pInfo.Controls.Add(new Label {
    Text="MODE:    Members will be MERGED (target's existing members kept)",
    Left=12, Top=60, Width=668, Height=16,
    Font=fMono, ForeColor=cGreenTxt, BackColor=Color.Transparent
});
form.Controls.Add(pInfo);

// ── filter row ────────────────────────────────────────────────────
var pFilt = new Panel { Left=12, Top=160, Width=692, Height=32, BackColor=cBG };
Action<Button,Color,Color> MkBtn = (b, bg, fg) => {
    b.BackColor=bg; b.ForeColor=fg; b.FlatStyle=FlatStyle.Flat;
    b.FlatAppearance.BorderSize=0; b.Cursor=Cursors.Hand; b.Font=fSmall;
};
var txtFilt = new TextBox {
    Left=0, Top=4, Width=230, Height=24,
    BackColor=cBlue, ForeColor=cText, Font=fBody,
    BorderStyle=BorderStyle.FixedSingle
};
var btnAll   = new Button { Text="✓ All",       Left=238, Top=4, Width=66, Height=24 };
var btnNone  = new Button { Text="✗ None",      Left=310, Top=4, Width=66, Height=24 };
var btnMatch = new Button { Text="● New members",Left=382, Top=4, Width=100, Height=24 };
MkBtn(btnAll,   cBlue, cText);
MkBtn(btnNone,  cBlue, cText);
MkBtn(btnMatch, Color.FromArgb(25, 60, 25), cGreenTxt);
pFilt.Controls.Add(txtFilt);
pFilt.Controls.Add(btnAll);
pFilt.Controls.Add(btnNone);
pFilt.Controls.Add(btnMatch);
form.Controls.Add(pFilt);

// ── checkedlistbox owner-drawn ───────────────────────────────────
var clb = new CheckedListBox {
    Left=12, Top=200, Width=692, Height=358,
    BackColor=cCard, ForeColor=cText, Font=fMono,
    BorderStyle=BorderStyle.FixedSingle,
    CheckOnClick=true,
    DrawMode=DrawMode.OwnerDrawFixed,
    ItemHeight=24
};

// add items — pre-tick MATCH rows
foreach (var rn in roleNames) {
    bool preTick = roleStatus[rn] == "MATCH";
    clb.Items.Add(rn, preTick);
}

// owner draw
clb.DrawItem += (s2, de) => {
    if (de.Index < 0 || de.Index >= clb.Items.Count) return;
    var rn   = clb.Items[de.Index].ToString();
    var stat = roleStatus.ContainsKey(rn) ? roleStatus[rn] : "?";
    bool isSel = (de.State & DrawItemState.Selected) != 0;
    bool isChk = clb.GetItemChecked(de.Index);

    // row background
    Color bgCol;
    if (isSel) bgCol = cSelBg;
    else if (stat == "MATCH") bgCol = cMatchBg;
    else if (stat == "SYNC")  bgCol = cSyncBg;
    else bgCol = cMissBg;
    de.Graphics.FillRectangle(new SolidBrush(bgCol), de.Bounds);

    // manual checkbox (only for non-MISSING rows)
    var chkRect = new Rectangle(de.Bounds.Left + 4, de.Bounds.Top + 5, 13, 13);
    if (stat != "MISSING") {
        de.Graphics.DrawRectangle(new Pen(Color.FromArgb(150, 150, 180), 1), chkRect);
        if (isChk) {
            de.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(80, 130, 200)), chkRect);
            de.Graphics.DrawString("✓", new Font("Segoe UI", 7f, FontStyle.Bold),
                new SolidBrush(Color.White), de.Bounds.Left + 4, de.Bounds.Top + 4);
        }
    } else {
        // disabled visual
        de.Graphics.DrawRectangle(new Pen(Color.FromArgb(80, 50, 50), 1), chkRect);
        de.Graphics.DrawLine(new Pen(Color.FromArgb(180, 70, 70), 1),
            chkRect.Left + 2, chkRect.Top + 6,
            chkRect.Right - 2, chkRect.Top + 6);
    }

    // status badge
    string badge; Color badgeColor;
    if (stat == "MATCH")   { badge = "[+ MERGE ]"; badgeColor = cGreenTxt; }
    else if (stat == "SYNC"){ badge = "[ IN SYNC]"; badgeColor = cMuted; }
    else                    { badge = "[ MISSING]"; badgeColor = cRedTxt; }
    de.Graphics.DrawString(badge, fMono, new SolidBrush(badgeColor),
        de.Bounds.Left + 24, de.Bounds.Top + 4);

    // role name
    Color nameColor = stat == "MISSING" ? Color.FromArgb(130, 100, 100) : cText;
    de.Graphics.DrawString(rn, fMono, new SolidBrush(nameColor),
        de.Bounds.Left + 110, de.Bounds.Top + 4);

    // detail string
    int srcCnt = roleSourceMemCnt.ContainsKey(rn) ? roleSourceMemCnt[rn] : 0;
    int tgtCnt = roleTargetMemCnt.ContainsKey(rn) ? roleTargetMemCnt[rn] : 0;
    int newCnt = roleNewMemCnt.ContainsKey(rn)    ? roleNewMemCnt[rn]    : 0;
    string detail;
    if (stat == "MATCH")        detail = "  source: " + srcCnt + " · target: " + tgtCnt + "  →  + " + newCnt + " new members";
    else if (stat == "SYNC")    detail = "  source: " + srcCnt + " · target: " + tgtCnt + "  →  already in sync";
    else                        detail = "  source has " + srcCnt + " members  →  role missing in target, will skip";

    int nameW = (int)de.Graphics.MeasureString(rn, fMono).Width;
    de.Graphics.DrawString(detail, fSmall,
        new SolidBrush(stat == "MISSING" ? cRedTxt : cMuted),
        de.Bounds.Left + 110 + nameW + 2, de.Bounds.Top + 6);
};

// prevent ticking MISSING rows
clb.ItemCheck += (s2, e2) => {
    if (e2.Index < 0 || e2.Index >= clb.Items.Count) return;
    var rn = clb.Items[e2.Index].ToString();
    if (roleStatus.ContainsKey(rn) && roleStatus[rn] == "MISSING") {
        e2.NewValue = CheckState.Unchecked;  // block check
    }
};

form.Controls.Add(clb);

// ── status bar ────────────────────────────────────────────────────
var lblCount = new Label {
    Text = matchCount + " selected  |  " + matchCount + " with new members  |  " + syncCount + " in sync  |  " + missingCount + " missing",
    Left=12, Top=566, Width=692, Height=18,
    Font=fSmall, ForeColor=cGreen, BackColor=Color.Transparent
};
form.Controls.Add(lblCount);

// ── action row ────────────────────────────────────────────────────
var pAct = new Panel { Left=12, Top=592, Width=692, Height=48, BackColor=cBG };
var btnImport = new Button {
    Text="▶  Merge Members", Left=0, Top=4, Width=180, Height=36,
    BackColor=cAccent, ForeColor=cWhite, Font=fBold,
    FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand
};
btnImport.FlatAppearance.BorderSize=0;
var btnCancel = new Button {
    Text="Cancel", Left=188, Top=4, Width=100, Height=36,
    BackColor=cBlue, ForeColor=cText, Font=fBold,
    FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand
};
btnCancel.FlatAppearance.BorderSize=0;
var lblResult = new Label {
    Text="", Left=296, Top=12, Width=396, Height=22,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
};
pAct.Controls.Add(btnImport);
pAct.Controls.Add(btnCancel);
pAct.Controls.Add(lblResult);
form.Controls.Add(pAct);

// ── refresh count ─────────────────────────────────────────────────
System.Action refreshCount = () => {
    int sel = clb.CheckedItems.Count;
    int totSelectable = matchCount;  // only MATCH rows are selectable
    lblCount.Text      = sel + " selected  |  " + matchCount + " with new members  |  " + syncCount + " in sync  |  " + missingCount + " missing (skipped)";
    lblCount.ForeColor = sel > 0 ? cGreen : cMuted;
    btnImport.Enabled   = sel > 0;
    btnImport.BackColor = sel > 0 ? cAccent : cMuted;
};

clb.ItemCheck += (s2, e2) =>
    form.BeginInvoke((System.Action)(() => refreshCount()));

// ── filter ────────────────────────────────────────────────────────
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

btnAll.Click += (s2, e2) => {
    clb.BeginUpdate();
    for (int i = 0; i < clb.Items.Count; i++) {
        var rn = clb.Items[i].ToString();
        // skip MISSING — they can't be selected
        if (roleStatus.ContainsKey(rn) && roleStatus[rn] != "MISSING")
            clb.SetItemChecked(i, true);
    }
    clb.EndUpdate(); refreshCount();
};
btnNone.Click += (s2, e2) => {
    clb.BeginUpdate();
    for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false);
    clb.EndUpdate(); refreshCount();
};
btnMatch.Click += (s2, e2) => {
    // tick only MATCH rows (in sync rows un-ticked)
    clb.BeginUpdate();
    for (int i = 0; i < clb.Items.Count; i++) {
        var rn = clb.Items[i].ToString();
        clb.SetItemChecked(i, roleStatus.ContainsKey(rn) && roleStatus[rn] == "MATCH");
    }
    clb.EndUpdate(); refreshCount();
};

btnCancel.Click += (s2, e2) => form.Close();

// ── merge logic ───────────────────────────────────────────────────
btnImport.Click += (s2, e2) => {
    var selRoles = new List<string>();
    foreach (var it in clb.CheckedItems) selRoles.Add(it.ToString());
    if (selRoles.Count == 0) return;

    // count new members across all selected
    int totalNewMembers = 0;
    foreach (var rn in selRoles)
        if (roleNewMemCnt.ContainsKey(rn)) totalNewMembers += roleNewMemCnt[rn];

    // confirmation
    var confirmMsg =
        "─── MERGE MEMBERS CONFIRMATION ───\n\n" +
        "FILE:     " + System.IO.Path.GetFileName(selectedFile) + "\n" +
        "SOURCE:   " + sourceModel + "\n" +
        "BY:       " + exportedBy + "\n\n" +
        "        ↓ ↓ ↓\n\n" +
        "TARGET:   " + targetDb + "\n\n" +
        "SELECTED: " + selRoles.Count + " roles\n" +
        "ADDING:   " + totalNewMembers + " new members across these roles\n\n" +
        "PRESERVED:\n" +
        "  • Target's existing members  (kept, not removed)\n" +
        "  • Target's RLS DAX filters   (untouched)\n" +
        "  • Target's ModelPermission   (unchanged)\n\n" +
        "SKIPPED: " + missingCount + " roles not present in target\n\n" +
        "Continue?";

    var ok = MessageBox.Show(confirmMsg, "Confirm Member Merge",
        MessageBoxButtons.YesNo, MessageBoxIcon.Information);
    if (ok != DialogResult.Yes) return;

    btnImport.Enabled = false;
    btnImport.Text    = "Merging...";
    Application.DoEvents();

    try {
        // build TMSL operations — one createOrReplace per selected role
        // with: target's existing TablePermissions + ModelPermission + MERGED members
        var opsOut = new Newtonsoft.Json.Linq.JArray();

        foreach (var rn in selRoles) {
            var targetRole = Model.Roles[rn];
            if (targetRole == null) continue;  // shouldn't happen but safety

            // build merged members array
            var existingIds = targetRoleMembers[rn];
            var mergedMembers = new Newtonsoft.Json.Linq.JArray();

            // add all target's existing members first (preserve them)
            foreach (var m in targetRole.Members) {
                mergedMembers.Add(new Newtonsoft.Json.Linq.JObject(
                    new Newtonsoft.Json.Linq.JProperty("memberName",       m.MemberName),
                    new Newtonsoft.Json.Linq.JProperty("memberId",         m.MemberID),
                    new Newtonsoft.Json.Linq.JProperty("identityProvider", "AzureAD")
                ));
            }

            // add source members not already in target
            var srcMembers = roleSourceMembers[rn];
            foreach (var sm in srcMembers) {
                var smId = sm["memberId"] != null ? sm["memberId"].ToString() : "";
                if (string.IsNullOrEmpty(smId)) continue;
                if (existingIds.Contains(smId)) continue;  // already there
                var smName = sm["memberName"] != null ? sm["memberName"].ToString() : "";
                mergedMembers.Add(new Newtonsoft.Json.Linq.JObject(
                    new Newtonsoft.Json.Linq.JProperty("memberName",       smName),
                    new Newtonsoft.Json.Linq.JProperty("memberId",         smId),
                    new Newtonsoft.Json.Linq.JProperty("identityProvider", "AzureAD")
                ));
            }

            // preserve target's existing tablePermissions (RLS DAX)
            var tps = new Newtonsoft.Json.Linq.JArray();
            foreach (var tp in targetRole.TablePermissions) {
                if (string.IsNullOrEmpty(tp.FilterExpression)) continue;
                tps.Add(new Newtonsoft.Json.Linq.JObject(
                    new Newtonsoft.Json.Linq.JProperty("name",             tp.Table.Name),
                    new Newtonsoft.Json.Linq.JProperty("filterExpression", tp.FilterExpression)
                ));
            }

            // build createOrReplace
            opsOut.Add(new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("createOrReplace", new Newtonsoft.Json.Linq.JObject(
                    new Newtonsoft.Json.Linq.JProperty("object", new Newtonsoft.Json.Linq.JObject(
                        new Newtonsoft.Json.Linq.JProperty("database", targetDb),
                        new Newtonsoft.Json.Linq.JProperty("role",     rn)
                    )),
                    new Newtonsoft.Json.Linq.JProperty("role", new Newtonsoft.Json.Linq.JObject(
                        new Newtonsoft.Json.Linq.JProperty("name",             rn),
                        new Newtonsoft.Json.Linq.JProperty("modelPermission",  targetRole.ModelPermission.ToString().ToLower()),
                        new Newtonsoft.Json.Linq.JProperty("tablePermissions", tps),
                        new Newtonsoft.Json.Linq.JProperty("members",          mergedMembers)
                    ))
                ))
            ));
        }

        var tmsl = new Newtonsoft.Json.Linq.JObject(
            new Newtonsoft.Json.Linq.JProperty("sequence", new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("operations", opsOut)
            ))
        ).ToString(Newtonsoft.Json.Formatting.Indented);

        ExecuteCommand(tmsl);

        // audit
        File.AppendAllText(System.IO.Path.Combine(folder, "audit.log"),
            System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
            " | MERGE MEMBERS | " + sourceModel + " -> " + targetDb +
            " | " + selRoles.Count + " roles, " + totalNewMembers + " new members" +
            (missingCount > 0 ? ", " + missingCount + " skipped" : "") +
            " | " + System.IO.Path.GetFileName(selectedFile) + "\r\n");

        // build missing-roles warning list
        var missingList = new List<string>();
        foreach (var rn in roleNames)
            if (roleStatus[rn] == "MISSING") missingList.Add(rn);

        // summary
        var summary =
            "Merge complete!\n\n" +
            sourceModel + "\n  →  " + targetDb + "\n\n" +
            "✓  " + selRoles.Count + " roles updated\n" +
            "✓  " + totalNewMembers + " new members added\n" +
            "✓  RLS DAX filters preserved\n";

        if (missingList.Count > 0) {
            summary += "\n⚠  " + missingList.Count + " roles skipped (not in target):\n";
            int show = System.Math.Min(missingList.Count, 15);
            for (int i = 0; i < show; i++)
                summary += "   • " + missingList[i] + "\n";
            if (missingList.Count > show)
                summary += "   ... and " + (missingList.Count - show) + " more (see audit.log)\n";

            // also write missing list to separate log
            var missingLog = System.IO.Path.Combine(folder,
                "missing_roles_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(missingLog,
                "Roles in source file not found in target model: " + targetDb + "\n" +
                "Source file: " + System.IO.Path.GetFileName(selectedFile) + "\n" +
                "Timestamp:   " + System.DateTime.Now.ToString("o") + "\n" +
                "Count:       " + missingList.Count + "\n\n" +
                string.Join("\n", missingList.ToArray()));
            summary += "\nFull list saved to:\n" + System.IO.Path.GetFileName(missingLog);
        }

        summary += "\n\nVerify: Service → Workspace → Model → Security";

        MessageBox.Show(summary, "Merge Complete",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        form.Close();
    }
    catch (System.Exception ex) {
        lblResult.ForeColor = cRed;
        lblResult.Text      = "✗ " + ex.Message.Substring(0, System.Math.Min(60, ex.Message.Length));
        btnImport.Enabled   = true;
        btnImport.Text      = "▶  Merge Members";
    }
};

form.ShowDialog();
