#!/usr/bin/env python3
import os
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import xml.etree.ElementTree as ET

from ToolUi import PALETTE, bind_common_shortcuts, setup_theme, style_text

class ResearchTreeEditor:
    def __init__(self, root):
        self.root = root
        setup_theme(self.root, "RimWorld Research Tree GUI Editor", "1400x800")
        
        # Grid scale config
        self.grid_size_x = 120  # Pixels per X coordinate
        self.grid_size_y = 60   # Pixels per Y coordinate
        self.node_width = 100
        self.node_height = 40
        
        # State
        self.file_path = None
        self.research_nodes = []  # List of dicts representing research defs
        self.selected_node_index = None
        self.dragged_node = None
        
        # Create Layout
        self.create_widgets()
        bind_common_shortcuts(self.root, save=self.save_file)
        
        # Try to load existing files in the workspace automatically if available
        self.auto_find_files()

    def create_widgets(self):
        # Top toolbar
        toolbar = ttk.Frame(self.root, padding=8, style="Toolbar.TFrame")
        toolbar.pack(side=tk.TOP, fill=tk.X)
        
        self.btn_load = ttk.Button(toolbar, text="Open XML File", command=self.load_file)
        self.btn_load.pack(side=tk.LEFT, padx=5)
        
        self.btn_save = ttk.Button(toolbar, text="Save XML File", command=self.save_file, style="Accent.TButton")
        self.btn_save.pack(side=tk.LEFT, padx=5)
        
        self.lbl_status = ttk.Label(toolbar, text="No file loaded", style="Status.TLabel")
        self.lbl_status.pack(side=tk.LEFT, padx=20)
        
        # Main split pane
        self.main_pane = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        self.main_pane.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)
        
        # Sidebar Frame (Left)
        sidebar = ttk.Frame(self.main_pane, width=250)
        self.main_pane.add(sidebar, weight=1)
        
        # Node List title & listbox
        ttk.Label(sidebar, text="Research Defs:", style="Title.TLabel").pack(anchor=tk.W, pady=5)
        
        list_frame = ttk.Frame(sidebar)
        list_frame.pack(fill=tk.BOTH, expand=True)
        
        self.node_listbox = tk.Listbox(list_frame, font=("Consolas", 10), exportselection=False)
        self.style_listbox(self.node_listbox)
        self.node_listbox.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.node_listbox.bind("<<ListboxSelect>>", self.on_listbox_select)
        
        scrollbar = ttk.Scrollbar(list_frame, orient=tk.VERTICAL, command=self.node_listbox.yview)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        self.node_listbox.config(yscrollcommand=scrollbar.set)
        
        # Add/Delete Buttons
        btn_frame = ttk.Frame(sidebar, padding=5)
        btn_frame.pack(fill=tk.X)
        
        self.btn_add = ttk.Button(btn_frame, text="Add New Def", command=self.add_node)
        self.btn_add.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)
        
        self.btn_delete = ttk.Button(btn_frame, text="Delete Def", command=self.delete_node, style="Danger.TButton")
        self.btn_delete.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)

        # Map Frame (Middle)
        map_frame = ttk.LabelFrame(self.main_pane, text="Visual Tree Map (Drag nodes to move, Click to select)")
        self.main_pane.add(map_frame, weight=3)
        
        # Canvas with scrollbars
        canvas_container = ttk.Frame(map_frame)
        canvas_container.pack(fill=tk.BOTH, expand=True)
        
        self.canvas = tk.Canvas(canvas_container, bg="#16202a", highlightthickness=0, scrollregion=(0, 0, 2000, 2000))
        self.canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        
        # Canvas scrollbars
        v_scroll = ttk.Scrollbar(canvas_container, orient=tk.VERTICAL, command=self.canvas.yview)
        v_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        h_scroll = ttk.Scrollbar(map_frame, orient=tk.HORIZONTAL, command=self.canvas.xview)
        h_scroll.pack(side=tk.BOTTOM, fill=tk.X)
        
        self.canvas.config(yscrollcommand=v_scroll.set, xscrollcommand=h_scroll.set)
        
        # Canvas bindings
        self.canvas.bind("<Button-1>", self.on_canvas_click)
        self.canvas.bind("<B1-Motion>", self.on_canvas_drag)
        self.canvas.bind("<ButtonRelease-1>", self.on_canvas_release)
        
        # Editor Frame (Right)
        self.editor_frame = ttk.LabelFrame(self.main_pane, text="Def Editor", padding=10)
        self.main_pane.add(self.editor_frame, weight=2)
        
        # Editor Form Fields
        row = 0
        ttk.Label(self.editor_frame, text="defName:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.ent_defname = ttk.Entry(self.editor_frame, width=30)
        self.ent_defname.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.ent_defname.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="label:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.ent_label = ttk.Entry(self.editor_frame, width=30)
        self.ent_label.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.ent_label.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="baseCost:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.ent_cost = ttk.Entry(self.editor_frame, width=30)
        self.ent_cost.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.ent_cost.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="techLevel:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.cb_tech = ttk.Combobox(self.editor_frame, values=["Neolithic", "Medieval", "Industrial", "Spacer", "Ultra", "Archotech"])
        self.cb_tech.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.cb_tech.bind("<<ComboboxSelected>>", lambda e: self.update_from_editor())
        self.cb_tech.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="tab:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.ent_tab = ttk.Entry(self.editor_frame, width=30)
        self.ent_tab.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.ent_tab.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="researchViewX:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.ent_x = ttk.Entry(self.editor_frame, width=30)
        self.ent_x.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.ent_x.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="researchViewY:").grid(row=row, column=0, sticky=tk.W, pady=3)
        self.ent_y = ttk.Entry(self.editor_frame, width=30)
        self.ent_y.grid(row=row, column=1, sticky=tk.EW, pady=3)
        self.ent_y.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        ttk.Label(self.editor_frame, text="description:").grid(row=row, column=0, sticky=tk.NW, pady=3)
        self.txt_desc = tk.Text(self.editor_frame, width=30, height=5, font=("Consolas", 9))
        self.txt_desc.grid(row=row, column=1, sticky=tk.NSEW, pady=3)
        style_text(self.txt_desc)
        self.txt_desc.bind("<KeyRelease>", lambda e: self.update_from_editor())
        
        row += 1
        # Prerequisites title and listbox
        ttk.Label(self.editor_frame, text="Prerequisites:\n(Select multiple)").grid(row=row, column=0, sticky=tk.NW, pady=3)
        
        prereq_frame = ttk.Frame(self.editor_frame)
        prereq_frame.grid(row=row, column=1, sticky=tk.NSEW, pady=3)
        
        self.prereq_listbox = tk.Listbox(prereq_frame, selectmode="multiple", height=8, font=("Consolas", 9), exportselection=False)
        self.style_listbox(self.prereq_listbox)
        self.prereq_listbox.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.prereq_listbox.bind("<<ListboxSelect>>", self.on_prereq_listbox_select)
        
        prereq_scroll = ttk.Scrollbar(prereq_frame, orient=tk.VERTICAL, command=self.prereq_listbox.yview)
        prereq_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.prereq_listbox.config(yscrollcommand=prereq_scroll.set)
        
        # Grid weight config for scaling editor
        self.editor_frame.grid_rowconfigure(row, weight=1)
        self.editor_frame.grid_columnconfigure(1, weight=1)

    def style_listbox(self, widget):
        widget.configure(
            bg=PALETTE["panel"],
            fg=PALETTE["text"],
            selectbackground=PALETTE["accent"],
            selectforeground="#ffffff",
            relief=tk.SOLID,
            bd=1,
            highlightthickness=1,
            highlightbackground=PALETTE["border"],
            highlightcolor=PALETTE["accent"],
            activestyle="none",
        )

    def auto_find_files(self):
        # Look for standard project research files
        possibilities = [
            "Defs/Research/ResearchWildWestDefs.xml",
            "Defs/Research/temp.xml"
        ]
        for p in possibilities:
            if os.path.exists(p):
                self.file_path = p
                self.load_nodes_from_path(p)
                break

    def load_file(self):
        initial_dir = os.path.join(os.getcwd(), "Defs", "Research")
        if not os.path.exists(initial_dir):
            initial_dir = os.getcwd()
            
        fp = filedialog.askopenfilename(
            initialdir=initial_dir,
            title="Open RimWorld Research XML Def File",
            filetypes=[("XML files", "*.xml"), ("All files", "*.*")]
        )
        if fp:
            self.file_path = fp
            self.load_nodes_from_path(fp)

    def load_nodes_from_path(self, path):
        try:
            tree = ET.parse(path)
            root_el = tree.getroot()
            
            nodes = []
            for child in root_el:
                if child.tag == "ResearchProjectDef":
                    defname = child.findtext("defName", "")
                    label = child.findtext("label", "")
                    desc = child.findtext("description", "")
                    base_cost = child.findtext("baseCost", "500")
                    tech_level = child.findtext("techLevel", "Industrial")
                    tab = child.findtext("tab", "HelodTechTab")
                    
                    x_str = child.findtext("researchViewX", "0")
                    y_str = child.findtext("researchViewY", "0")
                    
                    try:
                        x = float(x_str)
                    except ValueError:
                        x = 0.0
                    try:
                        y = float(y_str)
                    except ValueError:
                        y = 0.0
                    
                    prereqs = []
                    prereqs_el = child.find("prerequisites")
                    if prereqs_el is not None:
                        for li in prereqs_el.findall("li"):
                            if li.text:
                                prereqs.append(li.text.strip())
                    
                    nodes.append({
                        'defName': defname,
                        'label': label,
                        'description': desc,
                        'baseCost': base_cost,
                        'techLevel': tech_level,
                        'tab': tab,
                        'researchViewX': x,
                        'researchViewY': y,
                        'prerequisites': prereqs
                    })
            
            self.research_nodes = nodes
            self.lbl_status.config(text=f"Loaded: {os.path.basename(path)}")
            
            # Refresh UI
            self.refresh_all()
            
        except Exception as e:
            messagebox.showerror("Error", f"Failed to load XML file:\n{str(e)}")

    def save_file(self):
        if not self.file_path:
            initial_dir = os.path.join(os.getcwd(), "Defs", "Research")
            if not os.path.exists(initial_dir):
                initial_dir = os.getcwd()
            self.file_path = filedialog.asksaveasfilename(
                initialdir=initial_dir,
                title="Save RimWorld Research XML Def File",
                defaultextension=".xml",
                filetypes=[("XML files", "*.xml"), ("All files", "*.*")]
            )
            if not self.file_path:
                return

        try:
            # Build pretty XML string
            lines = ["<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<Defs>"]
            
            for node in self.research_nodes:
                lines.append("    <ResearchProjectDef>")
                lines.append(f"        <defName>{node['defName']}</defName>")
                lines.append(f"        <label>{node['label']}</label>")
                
                desc_val = node['description'].strip()
                if desc_val:
                    lines.append(f"        <description>{desc_val}</description>")
                else:
                    lines.append("        <description></description>")
                    
                lines.append(f"        <baseCost>{node['baseCost']}</baseCost>")
                lines.append(f"        <techLevel>{node['techLevel']}</techLevel>")
                
                if node['prerequisites']:
                    lines.append("        <prerequisites>")
                    for p in node['prerequisites']:
                        lines.append(f"            <li>{p}</li>")
                    lines.append("        </prerequisites>")
                
                lines.append(f"        <tab>{node['tab']}</tab>")
                
                # Format coordinates to integer or float nicely
                x = node['researchViewX']
                y = node['researchViewY']
                x_str = str(int(x)) if x.is_integer() else f"{x:.1f}"
                y_str = str(int(y)) if y.is_integer() else f"{y:.1f}"
                
                lines.append(f"        <researchViewX>{x_str}</researchViewX>")
                lines.append(f"        <researchViewY>{y_str}</researchViewY>")
                lines.append("    </ResearchProjectDef>")
                lines.append("")
                
            lines.append("</Defs>")
            
            # Write out to file
            with open(self.file_path, "w", encoding="utf-8") as f:
                f.write("\n".join(lines))
                
            messagebox.showinfo("Success", f"File saved successfully to:\n{self.file_path}")
            
        except Exception as e:
            messagebox.showerror("Error", f"Failed to save XML file:\n{str(e)}")

    def refresh_all(self):
        # Refresh sidebar listbox
        self.node_listbox.delete(0, tk.END)
        for node in self.research_nodes:
            self.node_listbox.insert(tk.END, f"{node['defName']} ({node['label']})")
            
        # Refresh prerequisites scroll list in editor
        self.prereq_listbox.delete(0, tk.END)
        for node in self.research_nodes:
            self.prereq_listbox.insert(tk.END, node['defName'])
            
        # Draw on Canvas
        self.draw_tree_map()
        
        # Load form
        self.load_selected_node_to_editor()

    def draw_tree_map(self):
        self.canvas.delete("all")
        
        # Draw coordinate grid
        for i in range(0, 20):
            x = i * self.grid_size_x + 50
            self.canvas.create_line(x, 0, x, 2000, fill="#263442", dash=(2, 4))
            self.canvas.create_text(x, 15, text=f"X:{i}", fill="#9fb0c0", font=("Consolas", 8))
            
        for j in range(0, 30):
            y = j * self.grid_size_y + 50
            self.canvas.create_line(0, y, 2000, y, fill="#263442", dash=(2, 4))
            self.canvas.create_text(15, y, text=f"Y:{j}", fill="#9fb0c0", font=("Consolas", 8))
            
        # Draw connections/arrows from prerequisites
        # Construct dictionary of node defName -> coordinate
        coords = {node['defName']: (node['researchViewX'], node['researchViewY']) for node in self.research_nodes}
        
        for node in self.research_nodes:
            defname = node['defName']
            curr_x, curr_y = node['researchViewX'], node['researchViewY']
            canvas_curr_x = curr_x * self.grid_size_x + 50
            canvas_curr_y = curr_y * self.grid_size_y + 50
            
            for prereq in node['prerequisites']:
                if prereq in coords:
                    p_x, p_y = coords[prereq]
                    canvas_p_x = p_x * self.grid_size_x + 50 + self.node_width/2
                    canvas_p_y = p_y * self.grid_size_y + 50
                    
                    dest_x = canvas_curr_x - self.node_width/2
                    dest_y = canvas_curr_y
                    
                    # Draw a straight dependency line.
                    self.canvas.create_line(
                        canvas_p_x, canvas_p_y,
                        dest_x, dest_y,
                        fill="#f59e0b", width=2, arrow=tk.LAST, arrowshape=(8,10,3)
                    )
        
        # Draw node boxes
        for index, node in enumerate(self.research_nodes):
            x, y = node['researchViewX'], node['researchViewY']
            canvas_x = x * self.grid_size_x + 50
            canvas_y = y * self.grid_size_y + 50
            
            # Colors
            if index == self.selected_node_index:
                box_color = "#2f6fed"
                border_color = "#ffffff"
                text_color = "#ffffff"
            else:
                box_color = "#24313f"
                border_color = "#60758b"
                text_color = "#ecf3fb"
                
            # Create rectangle
            box_id = self.canvas.create_rectangle(
                canvas_x - self.node_width/2, canvas_y - self.node_height/2,
                canvas_x + self.node_width/2, canvas_y + self.node_height/2,
                fill=box_color, outline=border_color, width=2,
                tags=("node", f"node_{index}")
            )
            
            # Label
            text_id = self.canvas.create_text(
                canvas_x, canvas_y,
                text=node['label'] if node['label'] else node['defName'],
                fill=text_color, font=("Helvetica", 9, "bold"),
                width=self.node_width - 10, justify=tk.CENTER,
                tags=("node", f"node_{index}")
            )

    def on_listbox_select(self, event):
        selection = self.node_listbox.curselection()
        if selection:
            self.selected_node_index = selection[0]
            self.load_selected_node_to_editor()
            self.draw_tree_map()

    def on_prereq_listbox_select(self, event):
        if self.selected_node_index is None:
            return
        
        node = self.research_nodes[self.selected_node_index]
        selected_prereqs = []
        for i in self.prereq_listbox.curselection():
            prereq_defname = self.prereq_listbox.get(i)
            # Cannot be a prerequisite of itself
            if prereq_defname != node['defName']:
                selected_prereqs.append(prereq_defname)
                
        node['prerequisites'] = selected_prereqs
        self.draw_tree_map()

    def load_selected_node_to_editor(self):
        if self.selected_node_index is None or self.selected_node_index >= len(self.research_nodes):
            # Clear editor
            self.ent_defname.delete(0, tk.END)
            self.ent_label.delete(0, tk.END)
            self.ent_cost.delete(0, tk.END)
            self.cb_tech.set("")
            self.ent_tab.delete(0, tk.END)
            self.ent_x.delete(0, tk.END)
            self.ent_y.delete(0, tk.END)
            self.txt_desc.delete("1.0", tk.END)
            self.prereq_listbox.selection_clear(0, tk.END)
            return
            
        node = self.research_nodes[self.selected_node_index]
        
        # Populate fields
        self.ent_defname.delete(0, tk.END)
        self.ent_defname.insert(0, node['defName'])
        
        self.ent_label.delete(0, tk.END)
        self.ent_label.insert(0, node['label'])
        
        self.ent_cost.delete(0, tk.END)
        self.ent_cost.insert(0, node['baseCost'])
        
        self.cb_tech.set(node['techLevel'])
        
        self.ent_tab.delete(0, tk.END)
        self.ent_tab.insert(0, node['tab'])
        
        # Format coordinates beautifully
        x = node['researchViewX']
        y = node['researchViewY']
        x_str = str(int(x)) if x.is_integer() else f"{x:.1f}"
        y_str = str(int(y)) if y.is_integer() else f"{y:.1f}"
        
        self.ent_x.delete(0, tk.END)
        self.ent_x.insert(0, x_str)
        
        self.ent_y.delete(0, tk.END)
        self.ent_y.insert(0, y_str)
        
        self.txt_desc.delete("1.0", tk.END)
        self.txt_desc.insert(tk.END, node['description'])
        
        # Set prerequisite selection in editor
        self.prereq_listbox.selection_clear(0, tk.END)
        for i in range(self.prereq_listbox.size()):
            prereq_def = self.prereq_listbox.get(i)
            if prereq_def in node['prerequisites']:
                self.prereq_listbox.selection_set(i)

    def update_from_editor(self):
        if self.selected_node_index is None:
            return
            
        node = self.research_nodes[self.selected_node_index]
        
        # Update model dict
        node['defName'] = self.ent_defname.get().strip()
        node['label'] = self.ent_label.get().strip()
        node['baseCost'] = self.ent_cost.get().strip()
        node['techLevel'] = self.cb_tech.get().strip()
        node['tab'] = self.ent_tab.get().strip()
        node['description'] = self.txt_desc.get("1.0", tk.END).strip()
        
        # Handle coordinates (X integer snapped, Y 0.8 units snapped & capped at 7.2)
        try:
            val_x = float(self.ent_x.get())
            node['researchViewX'] = float(round(val_x))
        except ValueError:
            pass
        try:
            val_y = float(self.ent_y.get())
            snap_y = round(round(val_y / 0.8) * 0.8, 1)
            node['researchViewY'] = max(0.0, min(7.2, snap_y))
        except ValueError:
            pass
            
        # Update node list in listbox
        self.node_listbox.delete(self.selected_node_index)
        self.node_listbox.insert(self.selected_node_index, f"{node['defName']} ({node['label']})")
        self.node_listbox.selection_set(self.selected_node_index)
        
        # Redraw
        self.draw_tree_map()

    def add_node(self):
        new_index = len(self.research_nodes)
        new_defname = f"HelodNewResearch_{new_index}"
        
        new_node = {
            'defName': new_defname,
            'label': "New Research",
            'description': "",
            'baseCost': "500",
            'techLevel': "Industrial",
            'tab': "HelodTechTab",
            'researchViewX': 1.0,
            'researchViewY': 1.0,
            'prerequisites': []
        }
        
        self.research_nodes.append(new_node)
        self.selected_node_index = new_index
        
        self.refresh_all()
        
        # Focus selection in sidebar listbox
        self.node_listbox.selection_clear(0, tk.END)
        self.node_listbox.selection_set(new_index)
        self.node_listbox.see(new_index)

    def delete_node(self):
        if self.selected_node_index is None:
            return
            
        deleted_node = self.research_nodes[self.selected_node_index]
        deleted_defname = deleted_node['defName']
        
        if not messagebox.askyesno("Confirm Delete", f"Are you sure you want to delete {deleted_defname}?"):
            return
            
        # Remove from list
        del self.research_nodes[self.selected_node_index]
        
        # Clean up prerequisites of other nodes referencing deleted node
        for node in self.research_nodes:
            if deleted_defname in node['prerequisites']:
                node['prerequisites'].remove(deleted_defname)
                
        # Reset selection
        self.selected_node_index = None
        
        self.refresh_all()

    # Canvas Drag-and-Drop Handlers
    def on_canvas_click(self, event):
        # Convert canvas event coordinates considering scrolling
        canvas_x = self.canvas.canvasx(event.x)
        canvas_y = self.canvas.canvasy(event.y)
        
        # Find item clicked
        clicked_item = self.canvas.find_withtag("current")
        if not clicked_item:
            return
            
        tags = self.canvas.gettags(clicked_item[0])
        for tag in tags:
            if tag.startswith("node_"):
                index = int(tag.split("_")[1])
                self.selected_node_index = index
                self.node_listbox.selection_clear(0, tk.END)
                self.node_listbox.selection_set(index)
                self.node_listbox.see(index)
                
                self.load_selected_node_to_editor()
                self.draw_tree_map()
                
                # Setup dragging
                self.dragged_node = index
                break

    def on_canvas_drag(self, event):
        if self.dragged_node is None:
            return
            
        canvas_x = self.canvas.canvasx(event.x)
        canvas_y = self.canvas.canvasy(event.y)
        
        # Calculate raw coordinate on grid (smooth float)
        raw_grid_x = (canvas_x - 50) / self.grid_size_x
        raw_grid_y = (canvas_y - 50) / self.grid_size_y
        
        # Clamp boundaries during dragging (X >= 0, 0 <= Y <= 7.2)
        grid_x = max(0.0, raw_grid_x)
        grid_y = max(0.0, min(7.2, raw_grid_y))
        
        node = self.research_nodes[self.dragged_node]
        if node['researchViewX'] != grid_x or node['researchViewY'] != grid_y:
            node['researchViewX'] = grid_x
            node['researchViewY'] = grid_y
            
            # Show temporary float coordinates in the editor during dragging
            x_str = f"{grid_x:.1f}"
            y_str = f"{grid_y:.1f}"
            
            self.ent_x.delete(0, tk.END)
            self.ent_x.insert(0, x_str)
            self.ent_y.delete(0, tk.END)
            self.ent_y.insert(0, y_str)
            
            self.draw_tree_map()

    def on_canvas_release(self, event):
        if self.dragged_node is not None:
            node = self.research_nodes[self.dragged_node]
            
            # Snap X strictly to integer, Y strictly to 0.8 units and clamp upon release
            snap_x = float(round(node['researchViewX']))
            snap_y = round(round(node['researchViewY'] / 0.8) * 0.8, 1)
            snap_y = max(0.0, min(7.2, snap_y))
            
            node['researchViewX'] = snap_x
            node['researchViewY'] = snap_y
            
            # Update coordinate entries to clean representation (integer or float)
            x_str = str(int(snap_x)) if snap_x.is_integer() else f"{snap_x:.1f}"
            y_str = str(int(snap_y)) if snap_y.is_integer() else f"{snap_y:.1f}"
            
            self.ent_x.delete(0, tk.END)
            self.ent_x.insert(0, x_str)
            self.ent_y.delete(0, tk.END)
            self.ent_y.insert(0, y_str)
            
            self.dragged_node = None
            self.draw_tree_map()

if __name__ == "__main__":
    root = tk.Tk()
    app = ResearchTreeEditor(root)
    root.mainloop()
