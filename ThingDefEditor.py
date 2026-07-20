#!/usr/bin/env python3
import copy
import datetime
import os
import shutil
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import xml.etree.ElementTree as ET

from ToolUi import bind_common_shortcuts, setup_theme, style_text


COMMON_FIELDS = [
    ("defName", "defName"),
    ("label", "label"),
    ("description", "description"),
    ("thingClass", "thingClass"),
    ("category", "category"),
    ("tickerType", "tickerType"),
    ("stackLimit", "stackLimit"),
    ("size", "size"),
    ("rotatable", "rotatable"),
    ("designationCategory", "designationCategory"),
    ("uiOrder", "uiOrder"),
    ("techLevel", "techLevel"),
]

GRAPHIC_FIELDS = [
    ("texPath", "graphicData/texPath"),
    ("graphicClass", "graphicData/graphicClass"),
    ("drawSize", "graphicData/drawSize"),
    ("drawOffset", "graphicData/drawOffset"),
    ("shaderType", "graphicData/shaderType"),
]


class ThingDefEditor:
    def __init__(self, root):
        self.root = root
        setup_theme(self.root, "RimWorld ThingDef Editor", "1500x900")

        self.workspace = os.getcwd()
        self.defs_root = os.path.join(self.workspace, "Defs")
        self.file_path = None
        self.tree = None
        self.xml_root = None
        self.thingdefs = []
        self.selected_index = None
        self.loading_editor = False

        self.common_vars = {}
        self.graphic_vars = {}
        self.attr_vars = {}

        self.create_widgets()
        bind_common_shortcuts(
            self.root,
            save=self.save_file,
            reload_cmd=self.reload_file,
            focus_filter=lambda: self.filter_entry.focus_set(),
        )
        self.populate_file_tree()
        self.auto_open_first_thingdef_file()

    def create_widgets(self):
        toolbar = ttk.Frame(self.root, padding=8, style="Toolbar.TFrame")
        toolbar.pack(side=tk.TOP, fill=tk.X)

        ttk.Button(toolbar, text="XML 열기", command=self.open_file_dialog).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="저장", command=self.save_file, style="Accent.TButton").pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="새로고침", command=self.reload_file).pack(side=tk.LEFT, padx=3)
        ttk.Separator(toolbar, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=8)
        ttk.Button(toolbar, text="ThingDef 추가", command=self.add_thingdef).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="복제", command=self.duplicate_thingdef).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="삭제", command=self.delete_thingdef, style="Danger.TButton").pack(side=tk.LEFT, padx=3)

        self.status_var = tk.StringVar(value="XML 파일을 선택하세요.")
        ttk.Label(toolbar, textvariable=self.status_var, style="Status.TLabel").pack(side=tk.LEFT, padx=14)

        pane = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        pane.pack(fill=tk.BOTH, expand=True, padx=6, pady=6)

        left = ttk.Frame(pane, width=310)
        pane.add(left, weight=1)
        ttk.Label(left, text="Defs XML 파일", style="Title.TLabel").pack(anchor=tk.W, pady=(0, 6))
        self.file_tree = ttk.Treeview(left, show="tree", selectmode="browse")
        self.file_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        file_scroll = ttk.Scrollbar(left, orient=tk.VERTICAL, command=self.file_tree.yview)
        file_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.file_tree.configure(yscrollcommand=file_scroll.set)
        self.file_tree.bind("<<TreeviewSelect>>", self.on_file_tree_select)

        middle = ttk.Frame(pane, width=390)
        pane.add(middle, weight=1)
        ttk.Label(middle, text="파일 안 ThingDef", style="Title.TLabel").pack(anchor=tk.W, pady=(0, 6))
        filter_row = ttk.Frame(middle)
        filter_row.pack(fill=tk.X, pady=(0, 5))
        ttk.Label(filter_row, text="검색").pack(side=tk.LEFT)
        self.filter_var = tk.StringVar()
        self.filter_entry = ttk.Entry(filter_row, textvariable=self.filter_var)
        self.filter_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=5)
        self.filter_entry.bind("<KeyRelease>", lambda _event: self.refresh_thingdef_list())

        columns = ("defName", "label", "parent")
        self.thing_tree = ttk.Treeview(middle, columns=columns, show="headings", selectmode="browse")
        self.thing_tree.heading("defName", text="defName")
        self.thing_tree.heading("label", text="label")
        self.thing_tree.heading("parent", text="ParentName/Class")
        self.thing_tree.column("defName", width=150, anchor=tk.W)
        self.thing_tree.column("label", width=130, anchor=tk.W)
        self.thing_tree.column("parent", width=110, anchor=tk.W)
        self.thing_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        thing_scroll = ttk.Scrollbar(middle, orient=tk.VERTICAL, command=self.thing_tree.yview)
        thing_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.thing_tree.configure(yscrollcommand=thing_scroll.set)
        self.thing_tree.bind("<<TreeviewSelect>>", self.on_thing_select)

        right = ttk.Frame(pane, width=760)
        pane.add(right, weight=3)

        self.editor_tabs = ttk.Notebook(right)
        self.editor_tabs.pack(fill=tk.BOTH, expand=True)
        self.basic_tab = ttk.Frame(self.editor_tabs, padding=8)
        self.stats_tab = ttk.Frame(self.editor_tabs, padding=8)
        self.raw_tab = ttk.Frame(self.editor_tabs, padding=8)
        self.editor_tabs.add(self.basic_tab, text="기본/그래픽")
        self.editor_tabs.add(self.stats_tab, text="statBases / costList")
        self.editor_tabs.add(self.raw_tab, text="Raw XML")

        self.create_basic_tab()
        self.create_stats_tab()
        self.create_raw_tab()

    def create_basic_tab(self):
        self.basic_tab.columnconfigure(1, weight=1)
        row = 0

        attr_box = ttk.LabelFrame(self.basic_tab, text="ThingDef 속성", padding=8)
        attr_box.grid(row=row, column=0, columnspan=2, sticky=tk.EW, pady=(0, 8))
        attr_box.columnconfigure(1, weight=1)
        for col, attr_name in enumerate(("ParentName", "Class", "Name", "Abstract")):
            ttk.Label(attr_box, text=attr_name).grid(row=col // 2, column=(col % 2) * 2, sticky=tk.W, padx=(0, 5), pady=2)
            var = tk.StringVar()
            ent = ttk.Entry(attr_box, textvariable=var)
            ent.grid(row=col // 2, column=(col % 2) * 2 + 1, sticky=tk.EW, padx=(0, 12), pady=2)
            ent.bind("<KeyRelease>", self.on_basic_field_change)
            self.attr_vars[attr_name] = var

        row += 1
        fields_box = ttk.LabelFrame(self.basic_tab, text="자주 쓰는 필드", padding=8)
        fields_box.grid(row=row, column=0, columnspan=2, sticky=tk.NSEW, pady=(0, 8))
        fields_box.columnconfigure(1, weight=1)
        for i, (label, _path) in enumerate(COMMON_FIELDS):
            ttk.Label(fields_box, text=label).grid(row=i, column=0, sticky=tk.W, pady=2)
            if label == "description":
                txt = tk.Text(fields_box, height=5, font=("Consolas", 9), wrap=tk.WORD)
                txt.grid(row=i, column=1, sticky=tk.EW, pady=2)
                style_text(txt)
                txt.bind("<KeyRelease>", self.on_basic_field_change)
                self.common_vars[label] = txt
            else:
                var = tk.StringVar()
                ent = ttk.Entry(fields_box, textvariable=var)
                ent.grid(row=i, column=1, sticky=tk.EW, pady=2)
                ent.bind("<KeyRelease>", self.on_basic_field_change)
                self.common_vars[label] = var

        row += 1
        graphic_box = ttk.LabelFrame(self.basic_tab, text="graphicData", padding=8)
        graphic_box.grid(row=row, column=0, columnspan=2, sticky=tk.NSEW)
        graphic_box.columnconfigure(1, weight=1)
        for i, (label, _path) in enumerate(GRAPHIC_FIELDS):
            ttk.Label(graphic_box, text=label).grid(row=i, column=0, sticky=tk.W, pady=2)
            var = tk.StringVar()
            ent = ttk.Entry(graphic_box, textvariable=var)
            ent.grid(row=i, column=1, sticky=tk.EW, pady=2)
            ent.bind("<KeyRelease>", self.on_basic_field_change)
            self.graphic_vars[label] = var

        self.basic_tab.rowconfigure(1, weight=1)

    def create_stats_tab(self):
        top = ttk.PanedWindow(self.stats_tab, orient=tk.HORIZONTAL)
        top.pack(fill=tk.BOTH, expand=True)

        self.stat_tree = self.create_pair_editor(top, "statBases", self.add_stat, self.update_stat, self.delete_stat)
        self.cost_tree = self.create_pair_editor(top, "costList", self.add_cost, self.update_cost, self.delete_cost)

    def create_pair_editor(self, parent, title, add_cmd, update_cmd, delete_cmd):
        frame = ttk.LabelFrame(parent, text=title, padding=8)
        parent.add(frame, weight=1)
        tree = ttk.Treeview(frame, columns=("name", "value"), show="headings", selectmode="browse", height=12)
        tree.heading("name", text="이름")
        tree.heading("value", text="값")
        tree.column("name", width=170, anchor=tk.W)
        tree.column("value", width=120, anchor=tk.W)
        tree.pack(fill=tk.BOTH, expand=True)

        edit = ttk.Frame(frame)
        edit.pack(fill=tk.X, pady=(8, 0))
        name_var = tk.StringVar()
        value_var = tk.StringVar()
        ttk.Entry(edit, textvariable=name_var, width=22).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 4))
        ttk.Entry(edit, textvariable=value_var, width=14).pack(side=tk.LEFT, padx=(0, 4))
        ttk.Button(edit, text="추가", command=lambda: add_cmd(name_var.get(), value_var.get())).pack(side=tk.LEFT, padx=2)
        ttk.Button(edit, text="수정", command=lambda: update_cmd(tree, name_var.get(), value_var.get())).pack(side=tk.LEFT, padx=2)
        ttk.Button(edit, text="삭제", command=lambda: delete_cmd(tree)).pack(side=tk.LEFT, padx=2)

        def fill_entry(_event):
            selected = tree.selection()
            if not selected:
                return
            values = tree.item(selected[0], "values")
            name_var.set(values[0])
            value_var.set(values[1])

        tree.bind("<<TreeviewSelect>>", fill_entry)
        tree.name_var = name_var
        tree.value_var = value_var
        return tree

    def create_raw_tab(self):
        controls = ttk.Frame(self.raw_tab)
        controls.pack(fill=tk.X, pady=(0, 6))
        ttk.Button(controls, text="선택 ThingDef에서 다시 불러오기", command=self.load_raw_xml).pack(side=tk.LEFT, padx=3)
        ttk.Button(controls, text="Raw XML 적용", command=self.apply_raw_xml).pack(side=tk.LEFT, padx=3)

        self.raw_text = tk.Text(self.raw_tab, font=("Consolas", 10), wrap=tk.NONE, undo=True)
        self.raw_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        style_text(self.raw_text)
        y_scroll = ttk.Scrollbar(self.raw_tab, orient=tk.VERTICAL, command=self.raw_text.yview)
        y_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        x_scroll = ttk.Scrollbar(self.raw_tab, orient=tk.HORIZONTAL, command=self.raw_text.xview)
        x_scroll.pack(side=tk.BOTTOM, fill=tk.X)
        self.raw_text.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)

    def populate_file_tree(self):
        self.file_tree.delete(*self.file_tree.get_children())
        start = self.defs_root if os.path.isdir(self.defs_root) else self.workspace
        root_id = self.file_tree.insert("", tk.END, text=os.path.basename(start), open=True, values=(start,))
        self.add_directory_to_tree(root_id, start)

    def add_directory_to_tree(self, parent_id, directory):
        try:
            entries = sorted(os.listdir(directory), key=lambda name: (not os.path.isdir(os.path.join(directory, name)), name.lower()))
        except OSError:
            return
        for name in entries:
            path = os.path.join(directory, name)
            if os.path.isdir(path):
                if self.directory_has_xml(path):
                    item_id = self.file_tree.insert(parent_id, tk.END, text=name, open=False, values=(path,))
                    self.add_directory_to_tree(item_id, path)
            elif name.lower().endswith(".xml"):
                label = name
                count = self.count_thingdef_text(path)
                if count:
                    label = f"{name}  ({count})"
                self.file_tree.insert(parent_id, tk.END, text=label, values=(path,))

    def directory_has_xml(self, path):
        for _root, _dirs, files in os.walk(path):
            if any(name.lower().endswith(".xml") for name in files):
                return True
        return False

    def count_thingdef_text(self, path):
        try:
            with open(path, "r", encoding="utf-8-sig") as handle:
                return handle.read().count("<ThingDef")
        except OSError:
            return 0

    def auto_open_first_thingdef_file(self):
        if not os.path.isdir(self.defs_root):
            return
        for root_dir, _dirs, files in os.walk(self.defs_root):
            for name in files:
                if not name.lower().endswith(".xml"):
                    continue
                path = os.path.join(root_dir, name)
                if self.count_thingdef_text(path):
                    self.load_file(path)
                    return

    def open_file_dialog(self):
        initial_dir = self.defs_root if os.path.isdir(self.defs_root) else self.workspace
        path = filedialog.askopenfilename(
            initialdir=initial_dir,
            title="ThingDef XML 열기",
            filetypes=[("XML files", "*.xml"), ("All files", "*.*")],
        )
        if path:
            self.load_file(path)

    def on_file_tree_select(self, _event):
        selected = self.file_tree.selection()
        if not selected:
            return
        path_values = self.file_tree.item(selected[0], "values")
        if not path_values:
            return
        path = path_values[0]
        if os.path.isfile(path) and path.lower().endswith(".xml"):
            self.load_file(path)

    def load_file(self, path):
        try:
            parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
            self.tree = ET.parse(path, parser=parser)
            self.xml_root = self.tree.getroot()
        except ET.ParseError as exc:
            messagebox.showerror("XML 파싱 실패", f"{path}\n\n{exc}")
            return
        except OSError as exc:
            messagebox.showerror("파일 열기 실패", str(exc))
            return

        self.file_path = path
        self.selected_index = None
        self.thingdefs = [child for child in list(self.xml_root) if child.tag == "ThingDef"]
        self.status_var.set(f"{os.path.relpath(path, self.workspace)} - ThingDef {len(self.thingdefs)}개")
        self.refresh_thingdef_list()
        self.clear_editor()

    def reload_file(self):
        if self.file_path:
            self.load_file(self.file_path)

    def refresh_thingdef_list(self):
        self.thing_tree.delete(*self.thing_tree.get_children())
        query = self.filter_var.get().strip().lower()
        for index, thing in enumerate(self.thingdefs):
            def_name = self.child_text(thing, "defName")
            label = self.child_text(thing, "label")
            parent = thing.get("ParentName", "")
            class_name = thing.get("Class", "")
            parent_info = parent or class_name
            searchable = " ".join([def_name, label, parent_info]).lower()
            if query and query not in searchable:
                continue
            self.thing_tree.insert("", tk.END, iid=str(index), values=(def_name, label, parent_info))
        if self.selected_index is not None and self.thing_tree.exists(str(self.selected_index)):
            self.thing_tree.selection_set(str(self.selected_index))

    def on_thing_select(self, _event):
        selected = self.thing_tree.selection()
        if not selected:
            return
        self.selected_index = int(selected[0])
        self.load_selected_thingdef()

    def load_selected_thingdef(self):
        thing = self.current_thingdef()
        if thing is None:
            return
        self.loading_editor = True
        for attr, var in self.attr_vars.items():
            var.set(thing.get(attr, ""))
        for label, path in COMMON_FIELDS:
            value = self.get_path_text(thing, path)
            widget_or_var = self.common_vars[label]
            if isinstance(widget_or_var, tk.Text):
                widget_or_var.delete("1.0", tk.END)
                widget_or_var.insert("1.0", value)
            else:
                widget_or_var.set(value)
        for label, path in GRAPHIC_FIELDS:
            self.graphic_vars[label].set(self.get_path_text(thing, path))
        self.loading_editor = False
        self.refresh_pair_tree(self.stat_tree, self.get_or_create_child(thing, "statBases", create=False))
        self.refresh_pair_tree(self.cost_tree, self.get_or_create_child(thing, "costList", create=False))
        self.load_raw_xml()

    def clear_editor(self):
        self.loading_editor = True
        for var in self.attr_vars.values():
            var.set("")
        for widget_or_var in self.common_vars.values():
            if isinstance(widget_or_var, tk.Text):
                widget_or_var.delete("1.0", tk.END)
            else:
                widget_or_var.set("")
        for var in self.graphic_vars.values():
            var.set("")
        self.loading_editor = False
        self.stat_tree.delete(*self.stat_tree.get_children())
        self.cost_tree.delete(*self.cost_tree.get_children())
        self.raw_text.delete("1.0", tk.END)

    def on_basic_field_change(self, _event=None):
        if self.loading_editor:
            return
        thing = self.current_thingdef()
        if thing is None:
            return
        for attr, var in self.attr_vars.items():
            value = var.get().strip()
            if value:
                thing.set(attr, value)
            elif attr in thing.attrib:
                del thing.attrib[attr]
        for label, path in COMMON_FIELDS:
            widget_or_var = self.common_vars[label]
            value = widget_or_var.get("1.0", "end-1c") if isinstance(widget_or_var, tk.Text) else widget_or_var.get()
            self.set_path_text(thing, path, value)
        for label, path in GRAPHIC_FIELDS:
            self.set_path_text(thing, path, self.graphic_vars[label].get())
        self.refresh_thingdef_list()

    def refresh_pair_tree(self, tree, parent_el):
        tree.delete(*tree.get_children())
        if parent_el is None:
            return
        for child in list(parent_el):
            if not isinstance(child.tag, str):
                continue
            tree.insert("", tk.END, values=(child.tag, child.text or ""))

    def add_stat(self, name, value):
        self.add_pair("statBases", name, value, self.stat_tree)

    def update_stat(self, tree, name, value):
        self.update_pair("statBases", tree, name, value)

    def delete_stat(self, tree):
        self.delete_pair("statBases", tree)

    def add_cost(self, name, value):
        self.add_pair("costList", name, value, self.cost_tree)

    def update_cost(self, tree, name, value):
        self.update_pair("costList", tree, name, value)

    def delete_cost(self, tree):
        self.delete_pair("costList", tree)

    def add_pair(self, parent_name, name, value, tree):
        thing = self.current_thingdef()
        if thing is None or not name.strip():
            return
        parent = self.get_or_create_child(thing, parent_name, create=True)
        child = ET.SubElement(parent, name.strip())
        child.text = value.strip()
        self.refresh_pair_tree(tree, parent)
        self.load_raw_xml()

    def update_pair(self, parent_name, tree, name, value):
        thing = self.current_thingdef()
        selected = tree.selection()
        if thing is None or not selected or not name.strip():
            return
        old_name = tree.item(selected[0], "values")[0]
        parent = self.get_or_create_child(thing, parent_name, create=False)
        if parent is None:
            return
        child = parent.find(old_name)
        if child is None:
            return
        child.tag = name.strip()
        child.text = value.strip()
        self.refresh_pair_tree(tree, parent)
        self.load_raw_xml()

    def delete_pair(self, parent_name, tree):
        thing = self.current_thingdef()
        selected = tree.selection()
        if thing is None or not selected:
            return
        name = tree.item(selected[0], "values")[0]
        parent = self.get_or_create_child(thing, parent_name, create=False)
        if parent is None:
            return
        child = parent.find(name)
        if child is not None:
            parent.remove(child)
        self.refresh_pair_tree(tree, parent)
        self.load_raw_xml()

    def load_raw_xml(self):
        thing = self.current_thingdef()
        self.raw_text.delete("1.0", tk.END)
        if thing is None:
            return
        xml = ET.tostring(thing, encoding="unicode", short_empty_elements=True)
        self.raw_text.insert("1.0", self.pretty_fragment(xml))

    def apply_raw_xml(self):
        if self.xml_root is None or self.selected_index is None:
            return
        raw = self.raw_text.get("1.0", "end-1c").strip()
        if not raw:
            return
        try:
            replacement = ET.fromstring(raw)
        except ET.ParseError as exc:
            messagebox.showerror("Raw XML 적용 실패", str(exc))
            return
        if replacement.tag != "ThingDef":
            messagebox.showerror("Raw XML 적용 실패", "최상위 태그는 ThingDef여야 합니다.")
            return
        old = self.thingdefs[self.selected_index]
        root_children = list(self.xml_root)
        root_pos = root_children.index(old)
        self.xml_root.remove(old)
        self.xml_root.insert(root_pos, replacement)
        self.thingdefs[self.selected_index] = replacement
        self.load_selected_thingdef()
        self.refresh_thingdef_list()

    def add_thingdef(self):
        if self.xml_root is None:
            messagebox.showinfo("파일 필요", "먼저 XML 파일을 열어주세요.")
            return
        thing = ET.Element("ThingDef", {"ParentName": "ResourceBase"})
        ET.SubElement(thing, "defName").text = self.make_unique_defname("HD_NewThing")
        ET.SubElement(thing, "label").text = "new thing"
        ET.SubElement(thing, "description").text = ""
        self.xml_root.append(thing)
        self.thingdefs.append(thing)
        self.selected_index = len(self.thingdefs) - 1
        self.refresh_thingdef_list()
        self.select_thing_index(self.selected_index)

    def duplicate_thingdef(self):
        thing = self.current_thingdef()
        if self.xml_root is None or thing is None:
            return
        clone = copy.deepcopy(thing)
        original_name = self.child_text(clone, "defName") or "HD_Copy"
        self.set_path_text(clone, "defName", self.make_unique_defname(f"{original_name}_Copy"))
        self.xml_root.append(clone)
        self.thingdefs.append(clone)
        self.selected_index = len(self.thingdefs) - 1
        self.refresh_thingdef_list()
        self.select_thing_index(self.selected_index)

    def delete_thingdef(self):
        thing = self.current_thingdef()
        if self.xml_root is None or thing is None:
            return
        def_name = self.child_text(thing, "defName") or "(no defName)"
        if not messagebox.askyesno("ThingDef 삭제", f"{def_name} 을(를) 삭제할까요?"):
            return
        self.xml_root.remove(thing)
        del self.thingdefs[self.selected_index]
        self.selected_index = None
        self.refresh_thingdef_list()
        self.clear_editor()

    def select_thing_index(self, index):
        if not self.thing_tree.exists(str(index)):
            self.filter_var.set("")
            self.refresh_thingdef_list()
        self.thing_tree.selection_set(str(index))
        self.thing_tree.see(str(index))
        self.load_selected_thingdef()

    def save_file(self):
        if self.tree is None or self.file_path is None:
            return
        try:
            timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
            backup_path = f"{self.file_path}.{timestamp}.bak"
            shutil.copy2(self.file_path, backup_path)
            ET.indent(self.tree, space="  ")
            self.tree.write(self.file_path, encoding="utf-8", xml_declaration=True, short_empty_elements=True)
        except OSError as exc:
            messagebox.showerror("저장 실패", str(exc))
            return
        self.status_var.set(f"저장 완료: {os.path.relpath(self.file_path, self.workspace)}  / 백업: {os.path.basename(backup_path)}")
        self.populate_file_tree()

    def current_thingdef(self):
        if self.selected_index is None:
            return None
        if self.selected_index < 0 or self.selected_index >= len(self.thingdefs):
            return None
        return self.thingdefs[self.selected_index]

    def make_unique_defname(self, base):
        existing = {self.child_text(thing, "defName") for thing in self.thingdefs}
        if base not in existing:
            return base
        index = 1
        while f"{base}_{index}" in existing:
            index += 1
        return f"{base}_{index}"

    def child_text(self, element, child_name):
        child = element.find(child_name)
        return child.text or "" if child is not None else ""

    def get_path_text(self, element, path):
        current = element
        for part in path.split("/"):
            current = current.find(part)
            if current is None:
                return ""
        return current.text or ""

    def set_path_text(self, element, path, value):
        parts = path.split("/")
        parent = element
        for part in parts[:-1]:
            parent = self.get_or_create_child(parent, part, create=True)
        child = self.get_or_create_child(parent, parts[-1], create=True)
        child.text = value.strip()

    def get_or_create_child(self, element, child_name, create):
        child = element.find(child_name)
        if child is None and create:
            child = ET.SubElement(element, child_name)
        return child

    def pretty_fragment(self, xml):
        try:
            element = ET.fromstring(xml)
            ET.indent(element, space="  ")
            return ET.tostring(element, encoding="unicode", short_empty_elements=True)
        except ET.ParseError:
            return xml


if __name__ == "__main__":
    root = tk.Tk()
    app = ThingDefEditor(root)
    root.mainloop()
