#r "System.Drawing"

using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// ── colours ──────────────────────────────────────────────────────
var cBG     = Color.FromArgb(24, 24, 40);
var cPanel  = Color.FromArgb(32, 32, 52);
var cCard   = Color.FromArgb(40, 40, 64);
var cAccent = Color.FromArgb(243, 111, 33);
var cBlue   = Color.FromArgb(15, 52, 96);
var cGreen  = Color.FromArgb(46, 125, 50);
var cText   = Color.FromArgb(224, 224, 224);
var cMuted  = Color.FromArgb(120, 120, 140);
var cRed    = Color.FromArgb(198, 40, 40);
var cWhite  = Color.White;

// ── fonts ─────────────────────────────────────────────────────────
var fTitle = new Font("Segoe UI", 13f, FontStyle.Bold);
var fBold  = new Font("Segoe UI",  9f, FontStyle.Bold);
var fBody  = new Font("Segoe UI",  9f, FontStyle.Regular);
var fMono  = new Font("Consolas",  9f, FontStyle.Regular);
var fSmall = new Font("Segoe UI",  8f, FontStyle.Regular);

// ── build role list ───────────────────────────────────────────────
var allRoles = new List<string>();
foreach (var r in Model.Roles) allRoles.Add(r.Name);
allRoles.Sort();

// ── main form ─────────────────────────────────────────────────────
var form = new Form {
    Text            = "Selective Role Export  ·  " + Model.Database.Name,
    Width           = 620,
    Height          = 580,
    BackColor       = cBG,
    StartPosition   = FormStartPosition.CenterScreen,
    FormBorderStyle = FormBorderStyle.FixedSingle,
    MaximizeBox     = false
};

// ── header ────────────────────────────────────────────────────────
var pHeader = new Panel { Left=0, Top=0, Width=620, Height=56, BackColor=cPanel };
pHeader.Controls.Add(new Label {
    Text="Select Roles to Export", Left=16, Top=10, Width=400, Height=26,
    Font=fTitle, ForeColor=cAccent, BackColor=Color.Transparent
});
pHeader.Controls.Add(new Label {
    Text="Source: " + Model.Database.Name + "   ·   " + allRoles.Count + " roles total",
    Left=16, Top=36, Width=580, Height=16,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
});
form.Controls.Add(pHeader);

// ── filter row ────────────────────────────────────────────────────
var pFilt = new Panel { Left=16, Top=64, Width=586, Height=36, BackColor=cBG };
pFilt.Controls.Add(new Label {
    Text="Filter:", Left=0, Top=9, Width=42, Height=18,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
});
var txtFilter = new TextBox {
    Left=46, Top=5, Width=280, Height=26,
    BackColor=cBlue, ForeColor=cText, Font=fBody,
    BorderStyle=BorderStyle.FixedSingle
};
pFilt.Controls.Add(txtFilter);

Action<Button,Color,Color> MkBtn = (b, bg, fg) => {
    b.BackColor=bg; b.ForeColor=fg; b.FlatStyle=FlatStyle.Flat;
    b.FlatAppearance.BorderSize=0; b.Cursor=Cursors.Hand; b.Font=fSmall;
};
var btnAll  = new Button { Text="✓ All",  Left=334, Top=5, Width=64, Height=26 };
var btnNone = new Button { Text="✗ None", Left=404, Top=5, Width=64, Height=26 };
var btnStar = new Button { Text="★ New",  Left=474, Top=5, Width=96, Height=26 };
MkBtn(btnAll,  cBlue,   cText);
MkBtn(btnNone, cBlue,   cText);
MkBtn(btnStar, cAccent, cWhite);
pFilt.Controls.Add(btnAll);
pFilt.Controls.Add(btnNone);
pFilt.Controls.Add(btnStar);
form.Controls.Add(pFilt);

// ── role checklist ────────────────────────────────────────────────
var clb = new CheckedListBox {
    Left=16, Top=108, Width=586, Height=336,
    BackColor=cCard, ForeColor=cText, Font=fMono,
    BorderStyle=BorderStyle.FixedSingle,
    CheckOnClick=true
};
foreach (var r in allRoles) clb.Items.Add(r, false);
form.Controls.Add(clb);

// ── count label ───────────────────────────────────────────────────
var lblCount = new Label {
    Text="0 of " + allRoles.Count + " roles selected",
    Left=16, Top=452, Width=400, Height=18,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
};
form.Controls.Add(lblCount);

// ── action row ────────────────────────────────────────────────────
var pAct = new Panel { Left=16, Top=474, Width=586, Height=48, BackColor=cBG };
var btnExport = new Button {
    Text="▶  Export Selected", Left=0, Top=6, Width=200, Height=36,
    BackColor=cMuted, ForeColor=cWhite, Font=fBold,
    FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand, Enabled=false
};
btnExport.FlatAppearance.BorderSize=0;
var btnCancel = new Button {
    Text="Cancel", Left=208, Top=6, Width=100, Height=36,
    BackColor=cBlue, ForeColor=cText, Font=fBold,
    FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand
};
btnCancel.FlatAppearance.BorderSize=0;
var lblResult = new Label {
    Text="", Left=316, Top=14, Width=268, Height=22,
    Font=fSmall, ForeColor=cMuted, BackColor=Color.Transparent
};
pAct.Controls.Add(btnExport);
pAct.Controls.Add(btnCancel);
pAct.Controls.Add(lblResult);
form.Controls.Add(pAct);

// ── helpers ───────────────────────────────────────────────────────
var checkedCache = new HashSet<string>();

System.Action refreshCount = () => {
    var n = clb.CheckedItems.Count;
    lblCount.Text      = n + " of " + clb.Items.Count + " roles selected";
    lblCount.ForeColor = n > 0 ? cGreen : cMuted;
    btnExport.Enabled   = n > 0;
    btnExport.BackColor = n > 0 ? cAccent : cMuted;
};

System.Action<string> rebuildList = (q) => {
    checkedCache.Clear();
    foreach (var it in clb.CheckedItems) checkedCache.Add(it.ToString());
    clb.BeginUpdate(); clb.Items.Clear();
    foreach (var r in allRoles)
        if (q == "" || r.ToLower().Contains(q.ToLower()))
            clb.Items.Add(r, checkedCache.Contains(r));
    clb.EndUpdate();
    refreshCount();
};

clb.ItemCheck += (s, e) =>
    form.BeginInvoke((System.Action)(() => refreshCount()));

txtFilter.TextChanged += (s, e) => rebuildList(txtFilter.Text);

btnAll.Click += (s, e) => {
    clb.BeginUpdate();
    for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true);
    clb.EndUpdate(); refreshCount();
};
btnNone.Click += (s, e) => {
    clb.BeginUpdate();
    for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false);
    clb.EndUpdate(); refreshCount();
};

// ── star new: compare vs last export ─────────────────────────────
btnStar.Click += (s, e) => {
    var folder_ = @"C:\temp\PBIRoleCopy";
    var known   = new HashSet<string>();
    if (Directory.Exists(folder_)) {
        var files_ = Directory.GetFiles(folder_, "roles_*.json");
        if (files_.Length > 0) {
            System.Array.Sort(files_);
            try {
                var raw_  = File.ReadAllText(files_[files_.Length - 1]);
                var json_ = Newtonsoft.Json.Linq.JObject.Parse(raw_);
                var ops_  = json_["sequence"]["operations"] as Newtonsoft.Json.Linq.JArray;
                if (ops_ != null)
                    foreach (var op in ops_)
                        if (op["createOrReplace"] != null &&
                            op["createOrReplace"]["role"] != null &&
                            op["createOrReplace"]["role"]["name"] != null)
                            known.Add(op["createOrReplace"]["role"]["name"].ToString());
            } catch { }
        }
    }
    if (known.Count == 0) {
        lblResult.ForeColor = cMuted;
        lblResult.Text = "No previous export found in C:\\temp\\PBIRoleCopy\\";
        return;
    }
    txtFilter.Text = "";
    clb.BeginUpdate(); clb.Items.Clear();
    foreach (var r in allRoles)
        clb.Items.Add(r, !known.Contains(r));
    clb.EndUpdate();
    refreshCount();
    var nc = clb.CheckedItems.Count;
    lblResult.ForeColor = nc > 0 ? cGreen : cMuted;
    lblResult.Text = nc > 0 ? "★ " + nc + " new roles auto-selected" : "No new roles found";
};

btnCancel.Click += (s, e) => form.Close();

// ── export click ─────────────────────────────────────────────────
btnExport.Click += (s, e) => {
    var selected = new List<string>();
    foreach (var it in clb.CheckedItems) selected.Add(it.ToString());
    if (selected.Count == 0) return;

    btnExport.Enabled = false;
    btnExport.Text    = "Exporting...";
    Application.DoEvents();

    try {
        var folder_   = @"C:\temp\PBIRoleCopy";
        var srcModel  = Model.Database.Name;
        var safeName  = Regex.Replace(srcModel, @"[^A-Za-z0-9]", "_");
        var ts        = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var isPartial = selected.Count < Model.Roles.Count();
        var fileName  = "roles_" + safeName + (isPartial ? "_partial" : "") + "_" + ts + ".json";
        var filePath  = System.IO.Path.Combine(folder_, fileName);
        if (!Directory.Exists(folder_)) Directory.CreateDirectory(folder_);

        // build TMSL operations for selected roles only
        var ops = new Newtonsoft.Json.Linq.JArray();
        foreach (var role in Model.Roles) {
            if (!selected.Contains(role.Name)) continue;

            var members = new Newtonsoft.Json.Linq.JArray();
            foreach (var mem in role.Members)
                members.Add(new Newtonsoft.Json.Linq.JObject(
                    new Newtonsoft.Json.Linq.JProperty("memberName",       mem.MemberName),
                    new Newtonsoft.Json.Linq.JProperty("memberId",         mem.MemberID),
                    new Newtonsoft.Json.Linq.JProperty("identityProvider", "AzureAD")
                ));

            var tps = new Newtonsoft.Json.Linq.JArray();
            foreach (var tp in role.TablePermissions) {
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

        // write with metadata
        var envelope = new Newtonsoft.Json.Linq.JObject(
            new Newtonsoft.Json.Linq.JProperty("_meta", new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("sourceModel", srcModel),
                new Newtonsoft.Json.Linq.JProperty("exportedAt",  System.DateTime.Now.ToString("o")),
                new Newtonsoft.Json.Linq.JProperty("exportedBy",  System.Environment.UserName),
                new Newtonsoft.Json.Linq.JProperty("roleCount",   selected.Count),
                new Newtonsoft.Json.Linq.JProperty("totalRoles",  Model.Roles.Count()),
                new Newtonsoft.Json.Linq.JProperty("exportType",  isPartial ? "partial" : "full")
            )),
            new Newtonsoft.Json.Linq.JProperty("sequence", new Newtonsoft.Json.Linq.JObject(
                new Newtonsoft.Json.Linq.JProperty("operations", ops)
            ))
        );

        File.WriteAllText(filePath, envelope.ToString(Newtonsoft.Json.Formatting.Indented));
        File.AppendAllText(System.IO.Path.Combine(folder_, "audit.log"),
            System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | EXPORT | " +
            srcModel + " | " + selected.Count + " of " + Model.Roles.Count() +
            " roles | " + fileName + "\r\n");

        lblResult.ForeColor = cGreen;
        lblResult.Text      = "✓ " + selected.Count + " roles exported";

        MessageBox.Show(
            "Export complete!\n\n" +
            "Source:  " + srcModel + "\n" +
            "Roles:   " + selected.Count + " of " + Model.Roles.Count() + "\n" +
            "Type:    " + (isPartial ? "Partial" : "Full") + "\n" +
            "File:    " + fileName + "\n\n" +
            "Saved to:\n" + folder_ + "\n\n" +
            "Connect TE2 to each target model\nand run the Import action.",
            "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        form.Close();
    } catch (System.Exception ex) {
        lblResult.ForeColor = cRed;
        lblResult.Text      = "✗ " + ex.Message.Substring(0, System.Math.Min(50, ex.Message.Length));
        btnExport.Enabled   = true;
        btnExport.Text      = "▶  Export Selected";
    }
};

form.ShowDialog();
