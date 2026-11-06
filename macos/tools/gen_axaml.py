#!/usr/bin/env python3
"""Generate Avalonia MainWindow.axaml from the original WinForms Form1.Designer.cs.

Keeps names, positions, sizes, texts, colors and tab/table structure identical.
Usage: gen_axaml.py <Form1.Designer.cs> <out.axaml> <out handlers list>
"""
import re
import sys
from xml.sax.saxutils import quoteattr

src = open(sys.argv[1], encoding="utf-8-sig").read()
body = src[src.index("private void InitializeComponent()"):]
types, props, children, cells, spans, tips, events = {}, {}, {}, {}, {}, {}, []
for m in re.finditer(r"this\.(\w+) = new System\.Windows\.Forms\.(\w+)\(\)", body):
    types[m.group(1)] = m.group(2)
for m in re.finditer(r"^\s+this\.(\w+)\.(\w+) = (.+);\s*$", body, re.M):
    props.setdefault(m.group(1), {})[m.group(2)] = m.group(3)
for m in re.finditer(r"this\.(\w+)\.Controls\.Add\(this\.(\w+)(?:, (\d+), (\d+))?\)", body):
    children.setdefault(m.group(1), []).append(m.group(2))
    if m.group(3):
        cells[m.group(2)] = (int(m.group(3)), int(m.group(4)))
for m in re.finditer(r"SetColumnSpan\(this\.(\w+), (\d+)\)", body):
    spans[m.group(1)] = int(m.group(2))
for m in re.finditer(r'this\.toolTip\d\.SetToolTip\(this\.(\w+), "((?:[^"\\]|\\.)*)"\)', body):
    tips[m.group(1)] = m.group(2).encode().decode("unicode_escape")
for m in re.finditer(r"this\.(\w+)\.(Click|CheckedChanged|LinkClicked|SelectedIndexChanged) \+= new [\w.]+\(this\.(\w+)\)", body):
    events.append((m.group(1), m.group(2), m.group(3)))
col_styles = {n: re.findall(r"ColumnStyle\(System\.Windows\.Forms\.SizeType\.(\w+)(?:, ([\d.]+)F)?", body[body.index(f"this.{n}.ColumnStyles"):][:900]) for n in types if types[n] == "TableLayoutPanel"}
row_styles = {n: re.findall(r"RowStyle\(System\.Windows\.Forms\.SizeType\.(\w+)(?:, ([\d.]+)F)?", body[body.index(f"this.{n}.RowStyles"):][:900]) for n in types if types[n] == "TableLayoutPanel"}

NAMED = {"Black": "Black", "DimGray": "DimGray", "Gainsboro": "Gainsboro", "LightGray": "LightGray",
         "Red": "Red", "Silver": "Silver", "Transparent": "Transparent"}


def lit(v):
    return bytes(v[1:-1], "utf-8").decode("unicode_escape") if v.startswith('"') else v


def color(v):
    return NAMED[re.search(r"Color\.(\w+)", v).group(1)]


def nums(v):
    return [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?", v)]


def attrs(n, t, parent_kind):
    p, a = props.get(n, {}), {}
    a["Name"] = n
    if "Text" in p and t not in ("TextBox",):
        a["Text" if t in ("Label", "CheckBox", "RadioButton", "LinkLabel", "Button", "TabPage") else "Text"] = lit(p["Text"])
    if t == "TextBox":
        a["Text"] = lit(p.get("Text", '""'))
        if "MaxLength" in p:
            a["MaxLength"] = p["MaxLength"]
        if p.get("ReadOnly") == "true":
            a["IsReadOnly"] = "True"
    if "BackColor" in p:
        a["Background"] = color(p["BackColor"])
    if "ForeColor" in p:
        a["Foreground"] = color(p["ForeColor"])
    if p.get("Enabled") == "false":
        a["IsEnabled"] = "False"
    if p.get("Checked") == "true":
        a["IsChecked"] = "True"
    if "TextAlign" in p and "Center" in p["TextAlign"]:
        a["HAlign"] = "Center"
    if "Font" in p:
        size = nums(p["Font"])[0]
        a["FontSize"] = str(round(size * 96 / 72, 2))
        if "Bold" in p["Font"]:
            a["FontWeight"] = "Bold"
        if "Underline" in p["Font"]:
            a["Underline"] = "True"
    if p.get("BorderStyle", "").endswith("Fixed3D") or p.get("BorderStyle", "").endswith("FixedSingle"):
        a["Border"] = "True"
    if p.get("AutoEllipsis") == "true":
        a["Ellipsis"] = "True"
    if n in tips:
        a["Tip"] = tips[n]
    return a


out = []


def emit(n, ind, parent_kind):
    t = types[n]
    p = props.get(n, {})
    a = attrs(n, t, parent_kind)
    pos = ""
    if parent_kind == "canvas" and "Location" in p:
        x, y = nums(p["Location"])
        pos = f' Canvas.Left="{int(x)}" Canvas.Top="{int(y)}"'
    elif parent_kind == "grid" and n in cells:
        c, r = cells[n]
        pos = f' Grid.Column="{c}" Grid.Row="{r}"' + (f' Grid.ColumnSpan="{spans[n]}"' if n in spans else "")
    size = ""
    if "Size" in p and t not in ("TabControl", "TabPage", "TableLayoutPanel") and p.get("AutoSize") != "true":
        w, h = nums(p["Size"])
        size = f' Width="{int(w)}" Height="{int(h)}"'
    elif "Size" in p and t in ("CheckBox", "RadioButton", "Button"):
        pass
    sp = " " * ind
    q = lambda k, v: f" {k}={quoteattr(v)}"
    common = q("Name", n) + pos + size
    extra = ""
    if "Background" in a:
        extra += q("Background", a["Background"])
    if "Foreground" in a:
        extra += q("Foreground", a["Foreground"])
    if "IsEnabled" in a:
        extra += q("IsEnabled", "False")
    if "Tip" in a:
        extra += q("ToolTip.Tip", a["Tip"])
    if "FontSize" in a:
        extra += q("FontSize", a["FontSize"]) + (q("FontWeight", "Bold") if "FontWeight" in a else "")
    ev = "".join(q({"Click": "Click", "CheckedChanged": "IsCheckedChanged", "LinkClicked": "Click",
                    "SelectedIndexChanged": "SelectionChanged"}[e], h)
                 for (c, e, h) in events if c == n)
    if t == "Label":
        al = ' HorizontalContentAlignment="Center"' if "HAlign" in a else ""
        bd = ' BorderBrush="Gray" BorderThickness="1"' if "Border" in a else ""
        tr = ' TextTrimming="CharacterEllipsis"' if "Ellipsis" in a else ""
        out.append(f'{sp}<local:WfLabel{common}{pos and ""}{extra}{ev}{al}{bd}{tr} Text={quoteattr(a.get("Text", ""))}/>')
    elif t == "LinkLabel":
        out.append(f'{sp}<local:WfLink{common}{extra}{ev} Text={quoteattr(a.get("Text", ""))} Foreground="#0066CC" Cursor="Hand"/>')
    elif t == "Button":
        out.append(f'{sp}<Button{common}{extra}{ev} Content={quoteattr(a.get("Text", ""))} Classes="wf"/>')
    elif t in ("CheckBox", "RadioButton"):
        grp = f' GroupName="{parent_name[0]}"' if t == "RadioButton" else ""
        chk = ' IsChecked="True"' if "IsChecked" in a else ""
        out.append(f'{sp}<{t}{common}{extra}{ev}{grp}{chk} Content={quoteattr(a.get("Text", ""))}/>')
    elif t == "TextBox":
        ro = ' IsReadOnly="True"' if "IsReadOnly" in a else ""
        ml = f' MaxLength="{a["MaxLength"]}"' if "MaxLength" in a else ""
        out.append(f'{sp}<TextBox{common}{extra}{ro}{ml} Text={quoteattr(a.get("Text", ""))} Classes="wf"/>')
    elif t == "PictureBox":
        out.append(f'{sp}<Border{common} BorderBrush="Gray" BorderThickness="1" {extra.strip()}>'
                   f'<Image Name="{n}Img" Stretch="Fill"/></Border>')
    elif t == "ProgressBar":
        out.append(f'{sp}<ProgressBar{common} Minimum="0" Maximum="100" Value="0"/>')
    elif t == "TabControl":
        out.append(f'{sp}<TabControl{q("Name", n)}{pos}{ev}{extra} Classes="wf" Width="{int(nums(p["Size"])[0])}" Height="{int(nums(p["Size"])[1])}">')
        for c in children.get(n, []):
            emit(c, ind + 2, "tab")
        out.append(f"{sp}</TabControl>")
    elif t == "TabPage":
        out.append(f'{sp}<TabItem{q("Name", n)} Header={quoteattr(lit(p.get("Text", chr(34)*2)))}>')
        out.append(f'{sp}  <Canvas Background="{color(p["BackColor"]) if "BackColor" in p else "White"}">')
        parent_name[0] = n
        for c in children.get(n, []):
            emit(c, ind + 4, "canvas")
        out.append(f"{sp}  </Canvas>")
        out.append(f"{sp}</TabItem>")
    elif t == "TableLayoutPanel":
        cs = ",".join(("*" if k == "Percent" else v + "") if k == "Percent" else v for k, v in
                      [(k, (v or "0").rstrip("0").rstrip(".") if k == "Absolute" else v) for k, v in col_styles[n]])
        rs = ",".join("*" if k == "Percent" else (v or "0").rstrip("0").rstrip(".") for k, v in row_styles[n])
        cs = ",".join(f"{float(v):.4f}*" if k == "Percent" else f"{float(v):g}" for k, v in col_styles[n])
        rs = ",".join("*" if k == "Percent" else f"{float(v):g}" for k, v in row_styles[n])
        out.append(f'{sp}<Grid{q("Name", n)}{pos} ColumnDefinitions="{cs}" RowDefinitions="{rs}">')
        parent_name[0] = n
        for c in children.get(n, []):
            emit(c, ind + 2, "grid" if n == "MainTableLayout" or True else "canvas")
        out.append(f"{sp}</Grid>")
    else:
        sys.exit(f"unsupported control type {t} ({n})")


parent_name = [""]
emit("MainTableLayout", 4, "root")
dialogs = {n: {k: lit(v) for k, v in props[n].items() if k in ("Filter", "Title", "FileName")} for n, t in types.items() if t == "OpenFileDialog"}
form_text = lit(props.get("", {}).get("Text", "")) if False else re.search(r'this\.Text = "((?:[^"\\]|\\.)*)"', body).group(1).replace("\\'", "'")
cw, ch = re.search(r"this\.ClientSize = new System\.Drawing\.Size\((\d+), (\d+)\)", body).groups()
xaml = f'''<Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="using:WiiVCInjector"
        x:Class="WiiVCInjector.MainWindow" Width="{cw}" Height="{ch}" CanResize="False"
        Title={quoteattr(form_text)} Background="#F0F0F0" FontSize="11">
{chr(10).join(out)}
</Window>
'''
open(sys.argv[2], "w").write(xaml)
open(sys.argv[3], "w").write("\n".join(sorted({h for _, _, h in events})) + "\n--dialogs--\n" +
                             "\n".join(f"{n}\t{d}" for n, d in dialogs.items()) + "\n")
print(f"{len(types)} controls, {len(events)} events, {len(dialogs)} dialogs")
