﻿using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace DocSharp {
    /// <summary>
    /// Makes a bridge between the export process and the GUI.
    /// </summary>
    public class Exporter {
        readonly TaskEngine engine;
        TreeNode node;
        string path;
        int exported, exportables;

        /// <summary>
        /// Generate index.php fillers in the entire tree.
        /// </summary>
        public bool GenerateFillers { get; set; }

        /// <summary>
        /// Export public nodes.
        /// </summary>
        public bool ExportPublic { get; set; }

        /// <summary>
        /// Export internal nodes.
        /// </summary>
        public bool ExportInternal { get; set; }

        /// <summary>
        /// Export protected nodes.
        /// </summary>
        public bool ExportProtected { get; set; }

        /// <summary>
        /// Export private nodes.
        /// </summary>
        public bool ExportPrivate { get; set; }

        /// <summary>
        /// Create a page for each enum child node.
        /// </summary>
        public bool ExpandEnums { get; set; }

        /// <summary>
        /// Create a page for each struct child node.
        /// </summary>
        public bool ExpandStructs { get; set; }

        /// <summary>
        /// Makes a bridge between the export process and the GUI.
        /// </summary>
        /// <param name="engine">Task guard and GUI updater</param>
        public Exporter(TaskEngine engine) => this.engine = engine;

        /// <summary>
        /// Called when a node was exported.
        /// </summary>
        public void Ping(TreeNode node) {
            ++exported;
            engine.UpdateStatusLazy(string.Format("Exporting {0} ({1}%)...", node.Name,
                (exported * 100.0 / exportables).ToString("0.00")));
            engine.UpdateProgressBar(exported * 100 / exportables);
        }

        /// <summary>
        /// Set the exportability of the selected and all child nodes based on user preferences.
        /// </summary>
        /// <param name="node">Root node</param>
        void SetExportability(TreeNode node) {
            foreach (TreeNode child in node.Nodes) {
                ElementInfo tag = (ElementInfo)child.Tag;
                tag.Exportable = false;
                if (tag.Vis == Visibility.Default || (tag.Vis == Visibility.Public && ExportPublic) ||
                    (tag.Vis == Visibility.Internal && ExportInternal) || (tag.Vis == Visibility.Protected && ExportProtected) ||
                    (tag.Vis == Visibility.Private && ExportPrivate))
                    tag.Exportable = true;
                if (tag.Exportable) {
                    child.Tag = tag;
                    if (tag.Kind == Element.Enums) {
                        if (ExpandEnums)
                            SetExportability(child);
                    } else if (tag.Kind == Element.Structs) {
                        if (ExpandStructs)
                            SetExportability(child);
                    } else
                        SetExportability(child);
                }
            }
        }

        /// <summary>
        /// Count exportable nodes under a node.
        /// </summary>
        void Preprocess(TreeNode node) {
            if (node.Tag != null) {
                ElementInfo tag = (ElementInfo)node.Tag;
                ExportInfo export = tag.Export;
                export.summary = Utils.GetTag(tag.Summary, "summary", node);
                export.returns = Utils.GetTag(tag.Summary, "returns", node);
            }
            if (node.Tag == null || ((ElementInfo)node.Tag).Kind < Element.Functions)
                ++exportables;
            foreach (TreeNode child in node.Nodes)
                if (child.Tag == null || ((ElementInfo)child.Tag).Exportable)
                    Preprocess(child);
        }

        /// <summary>
        /// Background documentation generator.
        /// </summary>
        void Task() {
            engine.UpdateProgressBar(0);
            engine.UpdateStatus("Selecting nodes to export...");
            SetExportability(node);
            engine.UpdateStatus("Preprocessing nodes to export...");
            exportables = 0;
            Preprocess(node);
            exported = 0;
            engine.UpdateStatus(string.Format("Exporting {0} ({1}%)...", node.Text, 0f.ToString("0.00")));
            Design.GenerateDocumentation(node, path, this);
            if (GenerateFillers) {
                engine.UpdateStatus("Generating index.php fillers...");
                Utils.FillWithPHP(new DirectoryInfo(path));
            }
            engine.UpdateStatus("Finished!");
            engine.UpdateProgressBar(100);
            Process.Start(path);
        }

        /// <summary>
        /// Start the documentation generation.
        /// </summary>
        /// <param name="node">Program tree</param>
        /// <param name="path">Path to the documentation</param>
        public void GenerateDocumentation(TreeNode node, string path) {
            this.node = node;
            this.path = path;
            engine.Run(Task);
        }
    }
}