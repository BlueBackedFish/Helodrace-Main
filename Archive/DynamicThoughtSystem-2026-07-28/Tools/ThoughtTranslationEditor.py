#!/usr/bin/env python3
import datetime
import os
import re
import shutil
import tkinter as tk
from tkinter import messagebox, ttk
import xml.etree.ElementTree as ET

from ToolUi import bind_common_shortcuts, setup_theme, style_text


EXTENSION_CLASS = "Helodrace.ThoughtDefExtension_HelodPersonalityDescriptions"
THOUGHT_CLASSES = {
    "Helodrace.Thought_Memory_HelodPersonalityDescription",
    "Helodrace.Thought_Situational_HelodPersonalityDescription",
}

VARIANT_LABELS = [
    "00 Assertive + Passive",
    "01 Assertive + Cooperative",
    "02 Assertive + Independent",
    "03 Passive + Assertive",
    "04 Passive + Cooperative",
    "05 Passive + Independent",
    "06 Cooperative + Assertive",
    "07 Cooperative + Passive",
    "08 Cooperative + Independent",
    "09 Independent + Assertive",
    "10 Independent + Passive",
    "11 Independent + Cooperative",
    "12 PTSD + Assertive",
    "13 PTSD + Passive",
    "14 PTSD + Cooperative",
    "15 PTSD + Independent",
]


class ThoughtSource:
    def __init__(self, path, tree, root, def_name, source_el, kind):
        self.path = path
        self.tree = tree
        self.root = root
        self.def_name = def_name
        self.source_el = source_el
        self.kind = kind
        self.translation_path = None
        self.translations = {}
        self.translation_tree = None
        self.translation_root = None
        self.source_dirty = False
        self.translation_dirty = False


class ThoughtTranslationEditor:
    def __init__(self, root):
        self.root = root
        setup_theme(self.root, "Helod Thought Description Editor", "1650x950")

        self.workspace = os.getcwd()
        self.defs_root = os.path.join(self.workspace, "Defs")
        self.patches_root = os.path.join(self.workspace, "Patches")
        self.korean_keyed_root = os.path.join(self.workspace, "Languages", "Korean (한국어)", "Keyed")

        self.sources = []
        self.current = None
        self.loading = False

        self.create_widgets()
        bind_common_shortcuts(
            self.root,
            save=self.save_current,
            reload_cmd=self.reload_sources,
            focus_filter=lambda: self.filter_entry.focus_set(),
        )
        self.reload_sources()

    def create_widgets(self):
        toolbar = ttk.Frame(self.root, padding=8, style="Toolbar.TFrame")
        toolbar.pack(side=tk.TOP, fill=tk.X)

        ttk.Button(toolbar, text="스캔 새로고침", command=self.reload_sources).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="현재 저장", command=self.save_current, style="Accent.TButton").pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="모든 열린 파일 저장", command=self.save_all_loaded).pack(side=tk.LEFT, padx=3)
        ttk.Separator(toolbar, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=8)
        ttk.Button(toolbar, text="Def 16칸 보정", command=self.ensure_def_descriptions).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="한국어 키 16칸 보정", command=self.ensure_korean_keys).pack(side=tk.LEFT, padx=3)
        ttk.Button(toolbar, text="원문을 한국어 칸에 복사", command=self.copy_english_to_korean).pack(side=tk.LEFT, padx=3)

        self.status_var = tk.StringVar(value="ThoughtDef를 스캔하세요.")
        ttk.Label(toolbar, textvariable=self.status_var, style="Status.TLabel").pack(side=tk.LEFT, padx=14)

        pane = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        pane.pack(fill=tk.BOTH, expand=True, padx=6, pady=6)

        left = ttk.Frame(pane, width=520)
        pane.add(left, weight=2)
        ttk.Label(left, text="Thought sources", style="Title.TLabel").pack(anchor=tk.W, pady=(0, 6))

        filter_row = ttk.Frame(left)
        filter_row.pack(fill=tk.X, pady=(0, 5))
        ttk.Label(filter_row, text="검색").pack(side=tk.LEFT)
        self.filter_var = tk.StringVar()
        self.filter_var.trace_add("write", lambda *_args: self.refresh_source_tree())
        self.filter_entry = ttk.Entry(filter_row, textvariable=self.filter_var)
        self.filter_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=5)

        columns = ("def", "file", "en", "ko", "missing")
        self.source_tree = ttk.Treeview(left, columns=columns, show="headings", selectmode="browse")
        self.source_tree.heading("def", text="defName")
        self.source_tree.heading("file", text="Source file")
        self.source_tree.heading("en", text="Def")
        self.source_tree.heading("ko", text="KO")
        self.source_tree.heading("missing", text="Missing")
        self.source_tree.column("def", width=180, anchor=tk.W)
        self.source_tree.column("file", width=220, anchor=tk.W)
        self.source_tree.column("en", width=45, anchor=tk.CENTER)
        self.source_tree.column("ko", width=45, anchor=tk.CENTER)
        self.source_tree.column("missing", width=60, anchor=tk.CENTER)
        self.source_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        source_scroll = ttk.Scrollbar(left, orient=tk.VERTICAL, command=self.source_tree.yview)
        source_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.source_tree.configure(yscrollcommand=source_scroll.set)
        self.source_tree.bind("<<TreeviewSelect>>", self.on_source_select)

        right = ttk.Frame(pane, width=1100)
        pane.add(right, weight=5)
        right.columnconfigure(0, weight=1)
        right.columnconfigure(1, weight=1)
        right.rowconfigure(4, weight=1)

        meta = ttk.Frame(right)
        meta.grid(row=0, column=0, columnspan=2, sticky=tk.EW, pady=(0, 8))
        meta.columnconfigure(1, weight=1)
        meta.columnconfigure(3, weight=1)
        ttk.Label(meta, text="defName").grid(row=0, column=0, sticky=tk.W)
        self.def_name_var = tk.StringVar()
        ttk.Entry(meta, textvariable=self.def_name_var, state="readonly").grid(row=0, column=1, sticky=tk.EW, padx=5)
        ttk.Label(meta, text="Korean Keyed").grid(row=0, column=2, sticky=tk.W)
        self.translation_path_var = tk.StringVar()
        ttk.Entry(meta, textvariable=self.translation_path_var).grid(row=0, column=3, sticky=tk.EW, padx=5)

        ttk.Label(right, text="16 personality variants", font=("Segoe UI", 10, "bold")).grid(row=1, column=0, columnspan=2, sticky=tk.W)
        self.variant_var = tk.StringVar(value="0: " + VARIANT_LABELS[0])
        self.variant_combo = ttk.Combobox(
            right,
            textvariable=self.variant_var,
            values=[f"{i}: {label}" for i, label in enumerate(VARIANT_LABELS)],
            state="readonly",
        )
        self.variant_combo.grid(row=2, column=0, columnspan=2, sticky=tk.EW, pady=(2, 8))
        self.variant_combo.bind("<<ComboboxSelected>>", lambda _event: self.load_variant())

        ttk.Label(right, text="Def fallback English", font=("Segoe UI", 10, "bold")).grid(row=3, column=0, sticky=tk.W)
        ttk.Label(right, text="Korean keyed translation", font=("Segoe UI", 10, "bold")).grid(row=3, column=1, sticky=tk.W)

        self.english_text = tk.Text(right, height=20, font=("Consolas", 11), wrap=tk.WORD, undo=True)
        self.english_text.grid(row=4, column=0, sticky=tk.NSEW, padx=(0, 4))
        self.korean_text = tk.Text(right, height=20, font=("Consolas", 11), wrap=tk.WORD, undo=True)
        self.korean_text.grid(row=4, column=1, sticky=tk.NSEW, padx=(4, 0))
        style_text(self.english_text)
        style_text(self.korean_text)
        self.english_text.bind("<KeyRelease>", self.on_text_change)
        self.korean_text.bind("<KeyRelease>", self.on_text_change)

        bottom = ttk.Frame(right)
        bottom.grid(row=5, column=0, columnspan=2, sticky=tk.EW, pady=(8, 0))
        ttk.Button(bottom, text="이전", command=lambda: self.step_variant(-1)).pack(side=tk.LEFT, padx=3)
        ttk.Button(bottom, text="다음", command=lambda: self.step_variant(1)).pack(side=tk.LEFT, padx=3)
        ttk.Button(bottom, text="현재 원문 -> 한국어", command=self.copy_english_to_current_korean).pack(side=tk.LEFT, padx=3)
        ttk.Button(bottom, text="한국어 키 정렬", command=self.sort_korean_keys).pack(side=tk.LEFT, padx=3)

        self.detail_var = tk.StringVar(value="")
        ttk.Label(right, textvariable=self.detail_var, font=("Consolas", 10)).grid(row=6, column=0, columnspan=2, sticky=tk.W, pady=(6, 0))

    def reload_sources(self):
        self.sources = []
        for base in (self.defs_root, self.patches_root):
            if not os.path.isdir(base):
                continue
            for root_dir, _dirs, files in os.walk(base):
                for name in files:
                    if name.lower().endswith(".xml"):
                        self.scan_file(os.path.join(root_dir, name))
        self.sources.sort(key=lambda item: (os.path.relpath(item.path, self.workspace).lower(), item.def_name.lower()))
        for source in self.sources:
            self.load_translation(source)
        self.refresh_source_tree()
        self.status_var.set(f"스캔 완료: thought source {len(self.sources)}개")

    def scan_file(self, path):
        try:
            parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
            tree = ET.parse(path, parser=parser)
            root = tree.getroot()
        except (ET.ParseError, OSError):
            return

        for thought_el in root.iter("ThoughtDef"):
            def_name_el = thought_el.find("defName")
            if def_name_el is None or not (def_name_el.text or "").strip():
                continue
            def_name = def_name_el.text.strip()
            if self.is_personality_thought(thought_el):
                self.sources.append(ThoughtSource(path, tree, root, def_name, thought_el, "ThoughtDef"))

        for op_el in root.iter():
            xpath_el = op_el.find("xpath")
            if xpath_el is None or not xpath_el.text:
                continue
            def_name = self.def_name_from_xpath(xpath_el.text)
            if not def_name:
                continue
            if self.find_descriptions(op_el) is not None:
                self.sources.append(ThoughtSource(path, tree, root, def_name, op_el, "PatchOperation"))

    def is_personality_thought(self, thought_el):
        thought_class_el = thought_el.find("thoughtClass")
        if thought_class_el is not None and (thought_class_el.text or "").strip() in THOUGHT_CLASSES:
            return True
        return self.find_descriptions(thought_el) is not None

    def def_name_from_xpath(self, xpath):
        match = re.search(r"ThoughtDef\[defName\s*=\s*['\"]([^'\"]+)['\"]\]", xpath)
        return match.group(1) if match else None

    def refresh_source_tree(self):
        self.source_tree.delete(*self.source_tree.get_children())
        query = self.filter_var.get().strip().lower()
        for index, source in enumerate(self.sources):
            rel = os.path.relpath(source.path, self.workspace)
            en_count = len(self.get_descriptions(source))
            ko_count = self.count_korean_descriptions(source)
            missing = 16 - min(16, ko_count)
            text = f"{source.def_name} {rel}".lower()
            if query and query not in text:
                continue
            self.source_tree.insert("", tk.END, iid=str(index), values=(source.def_name, rel, en_count, ko_count, missing))

    def on_source_select(self, _event):
        selected = self.source_tree.selection()
        if not selected:
            return
        self.current = self.sources[int(selected[0])]
        self.load_translation(self.current)
        self.loading = True
        self.def_name_var.set(self.current.def_name)
        self.translation_path_var.set(os.path.relpath(self.current.translation_path, self.workspace))
        self.variant_var.set("0: " + VARIANT_LABELS[0])
        self.loading = False
        self.load_variant()
        self.update_detail()

    def load_variant(self):
        if self.current is None:
            return
        self.loading = True
        index = self.current_variant_index()
        descriptions = self.get_descriptions(self.current)
        english = descriptions[index].text if index < len(descriptions) and descriptions[index].text else ""
        korean = self.current.translations.get(self.translation_key(index), "")
        self.english_text.delete("1.0", tk.END)
        self.english_text.insert("1.0", english)
        self.korean_text.delete("1.0", tk.END)
        self.korean_text.insert("1.0", korean)
        self.loading = False
        self.update_detail()

    def on_text_change(self, _event=None):
        if self.loading or self.current is None:
            return
        index = self.current_variant_index()
        self.set_description_text(self.current, index, self.english_text.get("1.0", "end-1c"))
        self.current.translations[self.translation_key(index)] = self.korean_text.get("1.0", "end-1c")
        self.current.source_dirty = True
        self.current.translation_dirty = True
        self.update_detail()

    def current_variant_index(self):
        value = self.variant_var.get()
        try:
            return max(0, min(15, int(str(value).split(":", 1)[0])))
        except ValueError:
            return 0

    def step_variant(self, delta):
        index = self.current_variant_index()
        next_index = max(0, min(15, index + delta))
        self.variant_var.set(f"{next_index}: {VARIANT_LABELS[next_index]}")
        self.load_variant()

    def translation_key(self, index):
        return f"{self.current.def_name}.personalityDescriptions.{index}"

    def get_descriptions(self, source):
        descriptions_el = self.find_descriptions(source.source_el)
        if descriptions_el is None:
            return []
        return [child for child in list(descriptions_el) if child.tag == "li"]

    def find_descriptions(self, element):
        for li_el in element.iter("li"):
            if li_el.attrib.get("Class") == EXTENSION_CLASS:
                descriptions_el = li_el.find("descriptions")
                if descriptions_el is not None:
                    return descriptions_el
        return None

    def ensure_def_descriptions(self):
        if self.current is None:
            return
        descriptions_el = self.ensure_descriptions_element(self.current)
        existing = [child for child in list(descriptions_el) if child.tag == "li"]
        while len(existing) < 16:
            new_el = ET.Element("li")
            new_el.text = ""
            descriptions_el.append(new_el)
            existing.append(new_el)
        for extra in existing[16:]:
            descriptions_el.remove(extra)
        self.current.source_dirty = True
        self.load_variant()
        self.refresh_source_tree()
        self.status_var.set("Def fallback description 16칸을 맞췄습니다.")

    def ensure_descriptions_element(self, source):
        descriptions_el = self.find_descriptions(source.source_el)
        if descriptions_el is not None:
            return descriptions_el

        if source.kind == "ThoughtDef":
            mod_extensions = source.source_el.find("modExtensions")
            if mod_extensions is None:
                mod_extensions = ET.Element("modExtensions")
                stages = source.source_el.find("stages")
                if stages is not None:
                    source.source_el.insert(list(source.source_el).index(stages), mod_extensions)
                else:
                    source.source_el.append(mod_extensions)
        else:
            value = source.source_el.find("value")
            if value is None:
                value = ET.SubElement(source.source_el, "value")
            mod_extensions = value.find("modExtensions")
            if mod_extensions is None:
                mod_extensions = ET.SubElement(value, "modExtensions")

        extension = ET.SubElement(mod_extensions, "li", {"Class": EXTENSION_CLASS})
        descriptions_el = ET.SubElement(extension, "descriptions")
        return descriptions_el

    def set_description_text(self, source, index, text):
        descriptions_el = self.ensure_descriptions_element(source)
        items = [child for child in list(descriptions_el) if child.tag == "li"]
        while len(items) <= index:
            child = ET.Element("li")
            descriptions_el.append(child)
            items.append(child)
        items[index].text = text

    def ensure_korean_keys(self):
        if self.current is None:
            return
        self.load_translation(self.current)
        descriptions = self.get_descriptions(self.current)
        for index in range(16):
            key = self.translation_key(index)
            if key not in self.current.translations:
                value = descriptions[index].text if index < len(descriptions) and descriptions[index].text else ""
                self.current.translations[key] = value
                self.current.translation_dirty = True
        self.load_variant()
        self.refresh_source_tree()
        self.status_var.set("한국어 personalityDescriptions 16개 키를 맞췄습니다.")

    def copy_english_to_korean(self):
        if self.current is None:
            return
        descriptions = self.get_descriptions(self.current)
        for index in range(min(16, len(descriptions))):
            self.current.translations[self.translation_key(index)] = descriptions[index].text or ""
        self.current.translation_dirty = True
        self.load_variant()

    def copy_english_to_current_korean(self):
        if self.current is None:
            return
        self.korean_text.delete("1.0", tk.END)
        self.korean_text.insert("1.0", self.english_text.get("1.0", "end-1c"))
        self.on_text_change()

    def count_korean_descriptions(self, source):
        return sum(1 for index in range(16) if f"{source.def_name}.personalityDescriptions.{index}" in source.translations)

    def load_translation(self, source):
        if source.translation_path is None:
            source.translation_path = self.default_translation_path(source)
        if source.translation_tree is not None:
            return
        if not os.path.isfile(source.translation_path):
            root = ET.Element("LanguageData")
            source.translation_tree = ET.ElementTree(root)
            source.translation_root = root
            source.translations = {}
            return
        try:
            parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
            source.translation_tree = ET.parse(source.translation_path, parser=parser)
            source.translation_root = source.translation_tree.getroot()
            source.translations = {
                child.tag: child.text or ""
                for child in source.translation_root
                if isinstance(child.tag, str)
            }
        except (ET.ParseError, OSError) as exc:
            messagebox.showerror("번역 파일 열기 실패", f"{source.translation_path}\n\n{exc}")
            root = ET.Element("LanguageData")
            source.translation_tree = ET.ElementTree(root)
            source.translation_root = root
            source.translations = {}

    def default_translation_path(self, source):
        name = os.path.splitext(os.path.basename(source.path))[0]
        if "BlancasDrugs" in name:
            file_name = "Thoughts_BlancasDrugs.xml"
        elif name.startswith("Thoughts_"):
            file_name = name + ".xml"
        else:
            file_name = "Thoughts_" + name + ".xml"
        return os.path.join(self.korean_keyed_root, file_name)

    def sort_korean_keys(self):
        if self.current is None:
            return
        self.write_translation_tree(self.current, sort=True)
        self.load_variant()
        self.status_var.set("한국어 키를 정렬했습니다. 저장 전까지 디스크에는 반영되지 않습니다.")

    def write_translation_tree(self, source, sort=False):
        if source.translation_root is None:
            self.load_translation(source)
        existing = {child.tag: child for child in list(source.translation_root) if isinstance(child.tag, str)}
        for key, value in source.translations.items():
            element = existing.get(key)
            if element is None:
                element = ET.Element(key)
                source.translation_root.append(element)
                existing[key] = element
            element.text = value
        if sort:
            elements = [child for child in list(source.translation_root) if isinstance(child.tag, str)]
            for child in elements:
                source.translation_root.remove(child)
            for child in sorted(elements, key=lambda item: item.tag.lower()):
                source.translation_root.append(child)
            source.translation_dirty = True

    def save_current(self):
        if self.current is None:
            return
        self.on_text_change()
        self.save_source(self.current)
        self.refresh_source_tree()
        self.status_var.set("현재 Thought source와 한국어 Keyed를 저장했습니다.")

    def save_all_loaded(self):
        saved_paths = set()
        for source in self.sources:
            if source.source_dirty or source.translation_dirty:
                self.save_source(source, saved_paths)
        self.refresh_source_tree()
        self.status_var.set("로드된 Thought source와 한국어 Keyed를 저장했습니다.")

    def save_source(self, source, saved_paths=None):
        if saved_paths is None:
            saved_paths = set()
        self.write_translation_tree(source)
        targets = []
        if source.source_dirty:
            targets.append((source.path, source.tree, "source"))
        if source.translation_dirty:
            targets.append((source.translation_path, source.translation_tree, "translation"))
        if not targets:
            return
        for path, tree, kind in targets:
            if path in saved_paths:
                continue
            directory = os.path.dirname(path)
            if directory and not os.path.isdir(directory):
                os.makedirs(directory)
            if os.path.isfile(path):
                timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
                shutil.copy2(path, f"{path}.{timestamp}.bak")
            ET.indent(tree, space="  ")
            tree.write(path, encoding="utf-8", xml_declaration=True, short_empty_elements=False)
            saved_paths.add(path)
        source.source_dirty = False
        source.translation_dirty = False

    def update_detail(self):
        if self.current is None:
            self.detail_var.set("")
            return
        index = self.current_variant_index()
        ko_count = self.count_korean_descriptions(self.current)
        self.detail_var.set(
            f"{VARIANT_LABELS[index]} | Def descriptions: {len(self.get_descriptions(self.current))}/16 | "
            f"Korean keys: {ko_count}/16 | {os.path.relpath(self.current.path, self.workspace)}"
        )


if __name__ == "__main__":
    root = tk.Tk()
    app = ThoughtTranslationEditor(root)
    root.mainloop()
