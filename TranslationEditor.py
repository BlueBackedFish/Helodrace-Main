#!/usr/bin/env python3
import datetime
import os
import shutil
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import xml.etree.ElementTree as ET

from ToolUi import bind_common_shortcuts, setup_theme, style_text


SOURCE_FIELDS = [
    "label",
    "description",
    "jobString",
    "pawnSingular",
    "pawnsPlural",
    "leaderTitle",
    "labelNoun",
]


class TranslationEditor:
    def __init__(self, root):
        self.root = root
        setup_theme(self.root, "RimWorld Translation Editor", "1500x900")

        self.workspace = os.getcwd()
        self.languages_root = os.path.join(self.workspace, "Languages")
        self.defs_root = os.path.join(self.workspace, "Defs")

        self.file_path = None
        self.tree = None
        self.xml_root = None
        self.entries = []
        self.source_lookup = {}
        self.selected_index = None
        self.loading_editor = False

        self.create_widgets()
        bind_common_shortcuts(
            self.root,
            save=self.save_file,
            reload_cmd=self.reload_file,
            focus_filter=lambda: self.filter_entry.focus_set(),
        )
        self.populate_file_tree()
        self.auto_open_first_file()

    def create_widgets(self):
        toolbar = ttk.Frame(self.root, padding=8, style="Toolbar.TFrame")
        toolbar.pack(side=tk.TOP, fill=tk.X)

        ttk.Button(toolbar, text="번역 XML 열기", command=self.open_file_dialog).pack(side=tk.LEFT, padx=4)
        ttk.Button(toolbar, text="저장", command=self.save_file, style="Accent.TButton").pack(side=tk.LEFT, padx=4)
        ttk.Button(toolbar, text="새로고침", command=self.reload_file).pack(side=tk.LEFT, padx=3)
        ttk.Separator(toolbar, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=8)
        ttk.Button(toolbar, text="항목 추가", command=self.add_entry).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="선택 삭제", command=self.delete_entry, style="Danger.TButton").pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="Def에서 누락 키 추가", command=self.add_missing_from_defs).pack(side=tk.LEFT, padx=3)

        self.status_var = tk.StringVar(value="번역 XML 파일을 선택하세요.")
        ttk.Label(toolbar, textvariable=self.status_var, style="Status.TLabel").pack(side=tk.LEFT, padx=14)

        pane = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        pane.pack(fill=tk.BOTH, expand=True, padx=6, pady=6)

        left = ttk.Frame(pane, width=330)
        pane.add(left, weight=1)
        ttk.Label(left, text="Languages XML 파일", style="Title.TLabel").pack(anchor=tk.W, pady=(0, 6))
        self.file_tree = ttk.Treeview(left, show="tree", selectmode="browse")
        self.file_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        file_scroll = ttk.Scrollbar(left, orient=tk.VERTICAL, command=self.file_tree.yview)
        file_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.file_tree.configure(yscrollcommand=file_scroll.set)
        self.file_tree.bind("<<TreeviewSelect>>", self.on_file_tree_select)

        middle = ttk.Frame(pane, width=520)
        pane.add(middle, weight=2)
        ttk.Label(middle, text="번역 항목", style="Title.TLabel").pack(anchor=tk.W, pady=(0, 6))

        filter_row = ttk.Frame(middle)
        filter_row.pack(fill=tk.X, pady=(0, 5))
        ttk.Label(filter_row, text="검색").pack(side=tk.LEFT)
        self.filter_var = tk.StringVar()
        self.filter_entry = ttk.Entry(filter_row, textvariable=self.filter_var)
        self.filter_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=5)
        self.filter_entry.bind("<KeyRelease>", lambda _event: self.refresh_entry_tree())

        columns = ("key", "value", "source")
        self.entry_tree = ttk.Treeview(middle, columns=columns, show="headings", selectmode="browse")
        self.entry_tree.heading("key", text="Key")
        self.entry_tree.heading("value", text="Translation")
        self.entry_tree.heading("source", text="Source")
        self.entry_tree.column("key", width=210, anchor=tk.W)
        self.entry_tree.column("value", width=220, anchor=tk.W)
        self.entry_tree.column("source", width=90, anchor=tk.CENTER)
        self.entry_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        entry_scroll = ttk.Scrollbar(middle, orient=tk.VERTICAL, command=self.entry_tree.yview)
        entry_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.entry_tree.configure(yscrollcommand=entry_scroll.set)
        self.entry_tree.bind("<<TreeviewSelect>>", self.on_entry_select)

        right = ttk.Frame(pane, width=650)
        pane.add(right, weight=3)
        right.columnconfigure(0, weight=1)
        right.rowconfigure(5, weight=1)

        ttk.Label(right, text="Key", font=("Segoe UI", 10, "bold")).grid(row=0, column=0, sticky=tk.W)
        self.key_var = tk.StringVar()
        self.key_entry = ttk.Entry(right, textvariable=self.key_var, font=("Consolas", 10))
        self.key_entry.grid(row=1, column=0, sticky=tk.EW, pady=(2, 8))
        self.key_entry.bind("<KeyRelease>", self.on_editor_change)

        ttk.Label(right, text="원본 Def 값 / 참고", font=("Segoe UI", 10, "bold")).grid(row=2, column=0, sticky=tk.W)
        self.source_text = tk.Text(right, height=8, font=("Consolas", 10), wrap=tk.WORD, state=tk.DISABLED)
        self.source_text.grid(row=3, column=0, sticky=tk.EW, pady=(2, 8))
        style_text(self.source_text, readonly=True)

        ttk.Label(right, text="번역문", font=("Segoe UI", 10, "bold")).grid(row=4, column=0, sticky=tk.W)
        self.value_text = tk.Text(right, height=12, font=("Consolas", 11), wrap=tk.WORD, undo=True)
        self.value_text.grid(row=5, column=0, sticky=tk.NSEW, pady=(2, 8))
        style_text(self.value_text)
        self.value_text.bind("<KeyRelease>", self.on_editor_change)

        bottom = ttk.Frame(right)
        bottom.grid(row=6, column=0, sticky=tk.EW)
        ttk.Button(bottom, text="원본을 번역문에 복사", command=self.copy_source_to_value).pack(side=tk.LEFT, padx=3)
        ttk.Button(bottom, text="Key 기준 정렬", command=self.sort_entries).pack(side=tk.LEFT, padx=3)

    def populate_file_tree(self):
        self.file_tree.delete(*self.file_tree.get_children())
        start = self.languages_root if os.path.isdir(self.languages_root) else self.workspace
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
                    item_id = self.file_tree.insert(parent_id, tk.END, text=name, open=True, values=(path,))
                    self.add_directory_to_tree(item_id, path)
            elif name.lower().endswith(".xml"):
                count = self.count_language_entries(path)
                label = f"{name}  ({count})" if count else name
                self.file_tree.insert(parent_id, tk.END, text=label, values=(path,))

    def directory_has_xml(self, path):
        for _root, _dirs, files in os.walk(path):
            if any(name.lower().endswith(".xml") for name in files):
                return True
        return False

    def count_language_entries(self, path):
        try:
            root = ET.parse(path).getroot()
            return sum(1 for child in root if isinstance(child.tag, str))
        except (ET.ParseError, OSError):
            return 0

    def auto_open_first_file(self):
        if not os.path.isdir(self.languages_root):
            return
        for root_dir, _dirs, files in os.walk(self.languages_root):
            for name in files:
                if name.lower().endswith(".xml"):
                    self.load_file(os.path.join(root_dir, name))
                    return

    def open_file_dialog(self):
        initial_dir = self.languages_root if os.path.isdir(self.languages_root) else self.workspace
        path = filedialog.askopenfilename(
            initialdir=initial_dir,
            title="번역 XML 열기",
            filetypes=[("XML files", "*.xml"), ("All files", "*.*")],
        )
        if path:
            self.load_file(path)

    def on_file_tree_select(self, _event):
        selected = self.file_tree.selection()
        if not selected:
            return
        values = self.file_tree.item(selected[0], "values")
        if not values:
            return
        path = values[0]
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

        if self.xml_root.tag != "LanguageData":
            messagebox.showwarning("확인 필요", "최상위 태그가 LanguageData가 아닙니다.")

        self.file_path = path
        self.entries = [child for child in list(self.xml_root) if isinstance(child.tag, str)]
        self.selected_index = None
        self.source_lookup = self.build_source_lookup_for_translation(path)
        self.status_var.set(f"{os.path.relpath(path, self.workspace)} - 항목 {len(self.entries)}개")
        self.refresh_entry_tree()
        self.clear_editor()

    def reload_file(self):
        if self.file_path:
            self.load_file(self.file_path)

    def refresh_entry_tree(self):
        self.entry_tree.delete(*self.entry_tree.get_children())
        query = self.filter_var.get().strip().lower()
        for index, entry in enumerate(self.entries):
            key = entry.tag
            value = entry.text or ""
            source = "있음" if key in self.source_lookup else ""
            searchable = f"{key} {value} {self.source_lookup.get(key, '')}".lower()
            if query and query not in searchable:
                continue
            self.entry_tree.insert("", tk.END, iid=str(index), values=(key, self.one_line(value), source))
        if self.selected_index is not None and self.entry_tree.exists(str(self.selected_index)):
            self.entry_tree.selection_set(str(self.selected_index))

    def on_entry_select(self, _event):
        selected = self.entry_tree.selection()
        if not selected:
            return
        self.selected_index = int(selected[0])
        self.load_selected_entry()

    def load_selected_entry(self):
        entry = self.current_entry()
        if entry is None:
            return
        self.loading_editor = True
        self.key_var.set(entry.tag)
        self.value_text.delete("1.0", tk.END)
        self.value_text.insert("1.0", entry.text or "")
        self.set_source_text(self.source_lookup.get(entry.tag, ""))
        self.loading_editor = False

    def clear_editor(self):
        self.loading_editor = True
        self.key_var.set("")
        self.value_text.delete("1.0", tk.END)
        self.set_source_text("")
        self.loading_editor = False

    def on_editor_change(self, _event=None):
        if self.loading_editor:
            return
        entry = self.current_entry()
        if entry is None:
            return
        new_key = self.key_var.get().strip()
        if new_key and self.is_valid_xml_tag(new_key):
            entry.tag = new_key
        entry.text = self.value_text.get("1.0", "end-1c")
        self.set_source_text(self.source_lookup.get(entry.tag, ""))
        self.refresh_entry_tree()

    def add_entry(self):
        if self.xml_root is None:
            messagebox.showinfo("파일 필요", "먼저 번역 XML 파일을 열어주세요.")
            return
        key = self.make_unique_key("NewTranslationKey")
        entry = ET.Element(key)
        entry.text = ""
        self.xml_root.append(entry)
        self.entries.append(entry)
        self.selected_index = len(self.entries) - 1
        self.refresh_entry_tree()
        self.select_entry_index(self.selected_index)

    def delete_entry(self):
        entry = self.current_entry()
        if self.xml_root is None or entry is None:
            return
        if not messagebox.askyesno("번역 항목 삭제", f"{entry.tag} 을(를) 삭제할까요?"):
            return
        self.xml_root.remove(entry)
        del self.entries[self.selected_index]
        self.selected_index = None
        self.refresh_entry_tree()
        self.clear_editor()

    def add_missing_from_defs(self):
        if self.xml_root is None:
            return
        if not self.source_lookup:
            messagebox.showinfo("원본 없음", "이 파일 경로에서 대응되는 Def 원본을 찾지 못했습니다.")
            return
        existing = {entry.tag for entry in self.entries}
        added = 0
        for key, source in sorted(self.source_lookup.items()):
            if key in existing:
                continue
            entry = ET.Element(key)
            entry.text = source
            self.xml_root.append(entry)
            self.entries.append(entry)
            added += 1
        self.refresh_entry_tree()
        self.status_var.set(f"누락 키 {added}개 추가됨 - 저장 전까지 파일에는 반영되지 않습니다.")

    def copy_source_to_value(self):
        entry = self.current_entry()
        if entry is None:
            return
        source = self.source_lookup.get(entry.tag, "")
        if not source:
            return
        self.value_text.delete("1.0", tk.END)
        self.value_text.insert("1.0", source)
        self.on_editor_change()

    def sort_entries(self):
        if self.xml_root is None:
            return
        sorted_entries = sorted(self.entries, key=lambda entry: entry.tag.lower())
        for entry in self.entries:
            self.xml_root.remove(entry)
        for entry in sorted_entries:
            self.xml_root.append(entry)
        self.entries = sorted_entries
        self.selected_index = None
        self.refresh_entry_tree()
        self.clear_editor()

    def save_file(self):
        if self.tree is None or self.file_path is None:
            return
        try:
            timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
            backup_path = f"{self.file_path}.{timestamp}.bak"
            shutil.copy2(self.file_path, backup_path)
            ET.indent(self.tree, space="  ")
            self.tree.write(self.file_path, encoding="utf-8", xml_declaration=True, short_empty_elements=False)
        except OSError as exc:
            messagebox.showerror("저장 실패", str(exc))
            return
        self.status_var.set(f"저장 완료: {os.path.relpath(self.file_path, self.workspace)} / 백업: {os.path.basename(backup_path)}")
        self.populate_file_tree()

    def build_source_lookup_for_translation(self, translation_path):
        rel = os.path.relpath(translation_path, self.languages_root)
        parts = rel.split(os.sep)
        if len(parts) < 3:
            return self.build_english_keyed_lookup(parts)
        if parts[1] == "Keyed":
            return self.build_english_keyed_lookup(parts)
        if parts[1] != "DefInjected" or len(parts) < 4:
            return {}

        def_type = parts[2]
        file_name = parts[3]
        source_path = self.find_def_file(file_name)
        if not source_path:
            return {}
        try:
            parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
            source_root = ET.parse(source_path, parser=parser).getroot()
        except (ET.ParseError, OSError):
            return {}

        lookup = {}
        for def_el in source_root:
            if def_el.tag != def_type:
                continue
            def_name_el = def_el.find("defName")
            if def_name_el is None or not def_name_el.text:
                continue
            def_name = def_name_el.text.strip()
            for field in SOURCE_FIELDS:
                field_el = def_el.find(field)
                if field_el is not None and field_el.text:
                    lookup[f"{def_name}.{field}"] = field_el.text.strip()
        return lookup

    def build_english_keyed_lookup(self, rel_parts):
        if len(rel_parts) < 3 or rel_parts[1] != "Keyed":
            return {}
        english_path = os.path.join(self.languages_root, "English", "Keyed", rel_parts[2])
        if not os.path.isfile(english_path) or os.path.abspath(english_path) == os.path.abspath(self.file_path or ""):
            return {}
        try:
            english_root = ET.parse(english_path).getroot()
        except (ET.ParseError, OSError):
            return {}
        return {child.tag: child.text or "" for child in english_root if isinstance(child.tag, str)}

    def find_def_file(self, file_name):
        if not os.path.isdir(self.defs_root):
            return None
        for root_dir, _dirs, files in os.walk(self.defs_root):
            for name in files:
                if name.lower() == file_name.lower():
                    return os.path.join(root_dir, name)
        return None

    def set_source_text(self, value):
        self.source_text.configure(state=tk.NORMAL)
        self.source_text.delete("1.0", tk.END)
        self.source_text.insert("1.0", value)
        self.source_text.configure(state=tk.DISABLED)

    def current_entry(self):
        if self.selected_index is None:
            return None
        if self.selected_index < 0 or self.selected_index >= len(self.entries):
            return None
        return self.entries[self.selected_index]

    def select_entry_index(self, index):
        if not self.entry_tree.exists(str(index)):
            self.filter_var.set("")
            self.refresh_entry_tree()
        self.entry_tree.selection_set(str(index))
        self.entry_tree.see(str(index))
        self.load_selected_entry()

    def make_unique_key(self, base):
        existing = {entry.tag for entry in self.entries}
        if base not in existing:
            return base
        index = 1
        while f"{base}_{index}" in existing:
            index += 1
        return f"{base}_{index}"

    def is_valid_xml_tag(self, key):
        try:
            ET.fromstring(f"<{key}></{key}>")
            return True
        except ET.ParseError:
            return False

    def one_line(self, text):
        return " ".join((text or "").split())[:160]


if __name__ == "__main__":
    root = tk.Tk()
    app = TranslationEditor(root)
    root.mainloop()
