#!/usr/bin/env python3
"""Generate an Avalonia .axaml window from a WinForms *.Designer.cs file.

Keeps names, positions, sizes, texts, colors and tab/table structure identical.

Usage:
  gen_axaml.py <X.Designer.cs> <out.axaml> <handlers.txt> [--cls NS.Class] [--root Name|this] [--resx X.resx]

--root is the top level container: a control name (e.g. MainTableLayout, docked to fill) or
"this" to place the controls that the form itself owns on a Canvas.
"""
import argparse
import html
import re
import sys
from xml.sax.saxutils import quoteattr

ap = argparse.ArgumentParser()
ap.add_argument("designer")
ap.add_argument("out")
ap.add_argument("handlers")
ap.add_argument("--cls", default="WiiVCInjector.MainWindow")
ap.add_argument("--root", default="MainTableLayout")
ap.add_argument("--resx")
args = ap.parse_args()

src = open(args.designer, encoding="utf-8-sig").read()
body = src[src.index("private void InitializeComponent()"):]
body = re.sub(r'"\s*\+\s*\n\s*"', "", body)  # joined string literals
resx = {}
if args.resx:
    rx = open(args.resx, encoding="utf-8-sig").read()
    for m in re.finditer(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', rx, re.S):
        resx[m.group(1)] = html.unescape(m.group(2))

types, props, children, cells, spans, tips, events, items = {}, {}, {}, {}, {}, {}, [], {}
form_children = []
for m in re.finditer(r"this\.(\w+) = new System\.Windows\.Forms\.(\w+)\(\)", body):
    types[m.group(1)] = m.group(2)
for m in re.finditer(r"^\s+this\.(\w+)\.(\w+) = (.+);\s*$", body, re.M):
    props.setdefault(m.group(1), {})[m.group(2)] = m.group(3)
for m in re.finditer(r"^\s+this\.(\w+) = (.+);\s*$", body, re.M):
    props.setdefault("", {})[m.group(1)] = m.group(2)
for m in re.finditer(r"this\.(\w+)\.Controls\.Add\(this\.(\w+)(?:, (\d+), (\d+))?\)", body):
    children.setdefault(m.group(1), []).append(m.group(2))
    if m.group(3):
        cells[m.group(2)] = (int(m.group(3)), int(m.group(4)))
for m in re.finditer(r"^\s+this\.Controls\.Add\(this\.(\w+)\);", body, re.M):
    form_children.append(m.group(1))
for m in re.finditer(r"SetColumnSpan\(this\.(\w+), (\d+)\)", body):
    spans[m.group(1)] = int(m.group(2))
for m in re.finditer(r'this\.toolTip\d\.SetToolTip\(this\.(\w+), "((?:[^"\\]|\\.)*)"\)', body):
    tips[m.group(1)] = m.group(2).encode().decode("unicode_escape")
for m in re.finditer(r"this\.(\w+)\.Items\.AddRange\(new object\[\] \{(.*?)\}\);", body, re.S):
    items[m.group(1)] = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2))
EVENTS = "Click|CheckedChanged|LinkClicked|SelectedIndexChanged|Scroll|DoubleClick|Load"
for m in re.finditer(rf"this\.(\w+)\.({EVENTS}) \+= new [\w.]+\(this\.(\w+)\)", body):
    events.append((m.group(1), m.group(2), m.group(3)))
for m in re.finditer(rf"^\s+this\.({EVENTS}) \+= new [\w.]+\(this\.(\w+)\)", body, re.M):
    events.append(("", m.group(1), m.group(2)))


def styles(kind, n):
    cnt = int(props[n].get("ColumnCount" if kind == "Column" else "RowCount", "0"))
    found = re.findall(rf"{kind}Style\(System\.Windows\.Forms\.SizeType\.(\w+)(?:, ([\d.]+)F)?",
                       body[body.index(f"this.{n}.{kind}Styles"):][:1200])
    return found[:cnt]  # WinForms ignores styles beyond the declared count


def lit(v):
    m = re.fullmatch(r'resources\.GetString\("([^"]+)"\)', v)
    if m:
        return resx.get(m.group(1), "")
    if v.startswith('"'):
        return bytes(v[1:-1], "utf-8").decode("unicode_escape").replace("\\'", "'")
    return ""  # e.g. Settings.Default.X, filled in by code-behind


def color(v):
    if "SystemColors.Control" in v:
        return "#F0F0F0"
    m = re.search(r"Color\.FromArgb\((\d+), (\d+), (\d+)\)", v)
    if m:
        return "#%02X%02X%02X" % tuple(int(x) for x in m.groups())
    return re.search(r"Color\.(\w+)", v).group(1)


def nums(v):
    return [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?", v)]


out = []
q = lambda k, v: f" {k}={quoteattr(v)}"
EVMAP = {"Click": "Click", "CheckedChanged": "IsCheckedChanged", "LinkClicked": "Click",
         "SelectedIndexChanged": "SelectionChanged", "Scroll": "ValueChanged", "DoubleClick": "DoubleTapped"}


VERTICAL_INSET = 8  # plus the 3 px cell margin: 11 px above the radios and below the page
# Texts that describe Windows behaviour and are wrong on macOS
TEXT_OVERRIDES = {
    "WiiVMC": "Patch video mode (Wii Retail only, picks the mode when ticked, experimental)",
}
HEADER_INSET = 11  # header rows and tab strip line up with the content inside the pages (x = 14)


def emit(n, ind, parent_kind, group):
    t = types[n]
    p = props.get(n, {})
    sp = " " * ind
    pos = ""
    if parent_kind == "canvas" and "Location" in p:
        x, y = nums(p["Location"])
        pos = f' Canvas.Left="{int(x)}" Canvas.Top="{int(y)}"'
    elif parent_kind in ("grid", "cellgrid") and n in cells:
        c, r = cells[n]
        m = 6 if parent_kind == "cellgrid" else 3
        pos = (f' Grid.Column="{c}" Grid.Row="{r}"' + (f' Grid.ColumnSpan="{spans[n]}"' if n in spans else "")
               + (f' Margin="{m - HEADER_INSET},{m},{m - HEADER_INSET},{m}"' if types[n] == "TabControl" and group == "MainTableLayout" else
                  f' Margin="{m}"' if types[n] == "TabControl" else
                  f' Margin="{m}" HorizontalAlignment="Stretch" VerticalAlignment="Top"' if types[n] == "Button" and group == "MainTableLayout" else
                  f' Margin="{m},{m},0,0" HorizontalAlignment="Left" VerticalAlignment="Top"'))
    size = ""
    stretch = parent_kind in ("grid", "cellgrid") and t == "Button" and group == "MainTableLayout"
    if "Size" in p and t not in ("TableLayoutPanel", "TabPage") and (p.get("AutoSize") != "true" or t == "TabControl"):
        w, h = nums(p["Size"])
        if t == "TabControl" and group == "MainTableLayout":
            size = ""  # fills the row, so the bottom margin is the grid's, like the top one
        else:
            size = f' Height="{int(h)}"' if stretch else f' Width="{int(w)}" Height="{int(h)}"'
    elif t == "Button" and parent_kind == "canvas" and p.get("AutoSize") == "true":
        # AutoSize buttons (the two Save Key buttons) would shrink to the text; give them a real button size
        # and center them on the neighbouring 20 px text box
        size = ' Width="64" Height="22"'
        pos = re.sub(r'Canvas\.Top="(\d+)"', lambda m: f'Canvas.Top="{int(m.group(1)) - 1}"', pos)
    elif t == "TableLayoutPanel" and n == "MainTableLayout":
        size = ""  # fills the window; the header inset is a margin on the grid
    elif t == "TableLayoutPanel" and "Location" in p and "Size" in p:
        w, h = nums(p["Size"])
        size = f' Width="{int(w)}" Height="{int(h)}"'
    text = lit(p["Text"]) if "Text" in p else ""
    text = TEXT_OVERRIDES.get(n, text)
    common = q("Name", n) + pos + size
    extra = ""
    if "BackColor" in p and t != "TabPage":
        extra += q("Background", color(p["BackColor"]))
    if "ForeColor" in p:
        extra += q("Foreground", color(p["ForeColor"]))
    if p.get("Enabled") == "false":
        extra += q("IsEnabled", "False")
    if n in tips:
        extra += q("ToolTip.Tip", tips[n])
    if "Font" in p:
        extra += q("FontSize", str(round(nums(p["Font"])[0] * 96 / 72, 2)))
        if "Bold" in p["Font"]:
            extra += q("FontWeight", "Bold")
    ev = "".join(q(EVMAP[e], h) for (c, e, h) in events if c == n and e in EVMAP)
    ta = p.get("TextAlign", "")
    if t == "Label":
        al = ' HorizontalContentAlignment="Center"' if "Center" in ta else ""
        boxed = "Fixed" in p.get("BorderStyle", "")
        # boxed labels act as read-only fields: center the text vertically and keep it off the border
        va = ' VerticalContentAlignment="Center"' if ("Middle" in ta or boxed) else ""
        bd = ' BorderBrush="Gray" BorderThickness="1" Padding="5,1,5,0"' if boxed else ""
        tr = ' TextTrimming="CharacterEllipsis"' if p.get("AutoEllipsis") == "true" else ""
        out.append(f'{sp}<local:WfLabel{common}{extra}{ev}{al}{va}{bd}{tr} Text={quoteattr(text)}/>')
    elif t == "LinkLabel":
        out.append(f'{sp}<local:WfLink{common}{extra}{ev} Text={quoteattr(text)} Foreground="#0066CC" Cursor="Hand"/>')
    elif t == "Button":
        out.append(f'{sp}<Button{common}{extra}{ev} Classes="wf"><TextBlock Text={quoteattr(text)} TextWrapping="Wrap" '
                   f'TextAlignment="Center" TextTrimming="None"/></Button>')
    elif t in ("CheckBox", "RadioButton"):
        grp = f' GroupName="{group}"' if t == "RadioButton" else ""
        chk = ' IsChecked="True"' if p.get("Checked") == "true" else ""
        out.append(f'{sp}<{t}{common}{extra}{ev}{grp}{chk} Content={quoteattr(text)}/>')
    elif t == "TextBox":
        ro = ' IsReadOnly="True"' if p.get("ReadOnly") == "true" else ""
        ml = f' MaxLength="{p["MaxLength"]}"' if "MaxLength" in p else ""
        out.append(f'{sp}<TextBox{common}{extra}{ro}{ml} Text={quoteattr(text)} Classes="wf"/>')
    elif t == "PictureBox":
        out.append(f'{sp}<Border{common} BorderBrush="Gray" BorderThickness="1"{extra}>'
                   f'<Image Name="{n}Img" Stretch="Fill"/></Border>')
    elif t == "ProgressBar":
        out.append(f'{sp}<ProgressBar{common} Minimum="0" Maximum="100" Value="0"/>')
    elif t == "ComboBox":
        out.append(f'{sp}<ComboBox{common}{extra}{ev} Classes="wf">')
        for it in items.get(n, []):
            out.append(f'{sp}  <ComboBoxItem Content={quoteattr(it)}/>')
        out.append(f"{sp}</ComboBox>")
    elif t == "CheckedListBox":
        out.append(f'{sp}<ListBox{common}{extra}{ev} Classes="wf" BorderBrush="Gray" BorderThickness="1">')
        out.append(f'{sp}  <ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel Orientation="Vertical"/></ItemsPanelTemplate></ListBox.ItemsPanel>')
        for i, it in enumerate(items.get(n, [])):
            out.append(f'{sp}  <CheckBox Name="{n}_{i}" Content={quoteattr(it)} Width="120"/>')
        out.append(f"{sp}</ListBox>")
    elif t == "TrackBar":
        mn, mx, v = (p.get(k, d) for k, d in (("Minimum", "0"), ("Maximum", "10"), ("Value", "0")))
        out.append(f'{sp}<Slider{common}{extra}{ev} Minimum="{mn}" Maximum="{mx}" Value="{v}" '
                   f'SmallChange="{p.get("SmallChange", "1")}" LargeChange="{p.get("LargeChange", "5")}"/>')
    elif t == "GroupBox":
        out.append(f'{sp}<Grid{common}>')
        out.append(f'{sp}  <Border Margin="0,7,0,0" BorderBrush="#DCDCDC" BorderThickness="1"/>')
        out.append(f'{sp}  <TextBlock Text={quoteattr(text)} Margin="8,0,0,0" Padding="2,0" HorizontalAlignment="Left" '
                   f'VerticalAlignment="Top" Background="#F0F0F0"/>')
        out.append(f'{sp}  <Canvas>')
        for c in children.get(n, []):
            emit(c, ind + 4, "canvas", n)
        out.append(f"{sp}  </Canvas>")
        out.append(f"{sp}</Grid>")
    elif t == "TabControl":
        out.append(f'{sp}<TabControl{common}{ev}{extra} Classes="wf">')
        for c in children.get(n, []):
            emit(c, ind + 2, "tab", n)
        out.append(f"{sp}</TabControl>")
    elif t == "TabPage":
        bg = color(p["BackColor"]) if "BackColor" in p else "White"
        out.append(f'{sp}<TabItem{q("Name", n)} Header={quoteattr(text)}>')
        # The page scrolls when fonts or the tab strip make the content area smaller than the design.
        tw, th = nums(p["Size"]) if "Size" in p else (0, 0)
        cvs = f' Width="{int(tw)}" Height="{int(th)}"' if tw else ""
        out.append(f'{sp}  <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" Background="{bg}">')
        out.append(f'{sp}    <Canvas Background="{bg}"{cvs}>')
        for c in children.get(n, []):
            emit(c, ind + 6, "canvas", n)
        out.append(f"{sp}    </Canvas>")
        out.append(f"{sp}  </ScrollViewer>")
        out.append(f"{sp}</TabItem>")
    elif t == "TableLayoutPanel":
        cs = ",".join(f"{float(v):.4f}*" if k == "Percent" else f"{float(v or 0):g}" for k, v in styles("Column", n))
        rs = ",".join("*" if k == "Percent" else f"{float(v or 0):g}" for k, v in styles("Row", n))
        cellb = "CellBorderStyle" in p
        inset = f' Margin="{HEADER_INSET},{VERTICAL_INSET},{HEADER_INSET},{VERTICAL_INSET}"' if n == "MainTableLayout" else ""
        out.append(f'{sp}<Grid{q("Name", n)}{pos}{size}{inset} ColumnDefinitions="{cs}" RowDefinitions="{rs}">')
        if cellb:
            for r in range(len(styles("Row", n))):
                for c in range(len(styles("Column", n))):
                    out.append(f'{sp}  <Border Grid.Column="{c}" Grid.Row="{r}" BorderBrush="#A0A0A0" BorderThickness="1"/>')
        for c in children.get(n, []):
            emit(c, ind + 2, "cellgrid" if cellb else "grid", n)
        out.append(f"{sp}</Grid>")
    else:
        sys.exit(f"unsupported control type {t} ({n})")


if args.root == "this":
    for c in form_children:
        emit(c, 6, "canvas", "")
    body_xaml = "    <Canvas>\n" + "\n".join(out) + "\n    </Canvas>"
else:
    emit(args.root, 4, "root", "")
    body_xaml = "\n".join(out)

form = props.get("", {})
form_text = lit(form["Text"]) if "Text" in form else ""
cw, ch = re.search(r"this\.ClientSize = new System\.Drawing\.Size\((\d+), (\d+)\)", body).groups()
dialogs = {n: {k: lit(v) for k, v in props[n].items() if k in ("Filter", "Title", "FileName")}
           for n, t in types.items() if t == "OpenFileDialog"}
SLACK = 24 if args.cls.endswith('MainWindow') else 0  # room for the tab strip metrics of the macOS fonts
xaml = f'''<Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="using:WiiVCInjector"
        x:Class="{args.cls}" Width="{cw}" Height="{int(ch) + SLACK}" MinWidth="{cw}" MinHeight="{ch}" CanResize="False"
        Title={quoteattr(form_text)} Background="#F0F0F0" FontSize="11">
{body_xaml}
</Window>
'''
open(args.out, "w").write(xaml)
open(args.handlers, "w").write("\n".join(sorted({h for _, _, h in events})) + "\n--dialogs--\n" +
                               "\n".join(f"{n}\t{d}" for n, d in dialogs.items()) + "\n")
print(f"{len(types)} controls, {len(events)} events, {len(dialogs)} dialogs")
