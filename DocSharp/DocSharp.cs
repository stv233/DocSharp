﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace DocSharp {
    /// <summary>
    /// The main window of Doc#.
    /// </summary>
    public partial class DocSharp : Form {
        readonly Font italic, underline;

        string lastLoaded = string.Empty;
        TreeNode import;

        /// <summary>
        /// Load code from a folder.
        /// </summary>
        void LoadRecent(string recent) {
            string newRecents = Properties.Settings.Default.Recents;
            if (newRecents.Length == 0)
                Properties.Settings.Default.Recents = recent;
            else {
                int recentPos = newRecents.IndexOf(recent);
                if (recentPos == -1)
                    Properties.Settings.Default.Recents = string.Format("{0}\n{1}", recent, newRecents);
                else
                    Properties.Settings.Default.Recents = string.Format("{0}\n{1}{2}", recent, newRecents.Substring(0, recentPos),
                        newRecents.Substring(recentPos + recent.Length + 1));
            }
            Properties.Settings.Default.Save();
            LoadRecents();
            LoadFrom(recent, defines.Text);
        }

        /// <summary>
        /// Load code from a folder which is on the recents list.
        /// </summary>
        void LoadRecent(object sender, EventArgs e) {
            LoadRecent(((ToolStripMenuItem)sender).Text);
        }

        /// <summary>
        /// Fill the recents list with the saved entries.
        /// </summary>
        void LoadRecents() {
            loadRecentToolStripMenuItem.DropDownItems.Clear();
            string[] recents = Properties.Settings.Default.Recents.Split('\n');
            for (int i = 0; i < recents.Length - 1; ++i) {
                ToolStripItem recent = new ToolStripMenuItem(recents[i]);
                if (Directory.Exists(recents[i]))
                    recent.Click += LoadRecent;
                else
                    recent.Enabled = false;
                loadRecentToolStripMenuItem.DropDownItems.Add(recent);
            }
        }

        /// <summary>
        /// Load settings and set up the window accordingly.
        /// </summary>
        public DocSharp() {
            InitializeComponent();
            italic = new Font(sourceInfo.Font, FontStyle.Italic);
            underline = new Font(sourceInfo.Font, FontStyle.Underline);
            LoadRecents();
            expandEnums.Checked = Properties.Settings.Default.ExpandEnums;
            expandStructs.Checked = Properties.Settings.Default.ExpandStructs;
            exportAttributes.Checked = Properties.Settings.Default.ExportAttributes;
            extension.Text = Properties.Settings.Default.FileExtension;
            defines.Text = Properties.Settings.Default.DefineConstants;
            phpFillers.Checked = Properties.Settings.Default.PhpFillers;
            exportInternal.Checked = Properties.Settings.Default.VisibilityInternal;
            exportPrivate.Checked = Properties.Settings.Default.VisibilityPrivate;
            exportProtected.Checked = Properties.Settings.Default.VisibilityProtected;
            exportPublic.Checked = Properties.Settings.Default.VisibilityPublic;
        }

        /// <summary>
        /// Save settings when Doc# is closed.
        /// </summary>
        private void DocSharp_FormClosed(object sender, FormClosedEventArgs e) {
            Properties.Settings.Default.ExpandEnums = expandEnums.Checked;
            Properties.Settings.Default.ExpandStructs = expandStructs.Checked;
            Properties.Settings.Default.ExportAttributes = exportAttributes.Checked;
            Properties.Settings.Default.FileExtension = extension.Text;
            Properties.Settings.Default.DefineConstants = defines.Text;
            Properties.Settings.Default.PhpFillers = phpFillers.Checked;
            Properties.Settings.Default.VisibilityInternal = exportInternal.Checked;
            Properties.Settings.Default.VisibilityPrivate = exportPrivate.Checked;
            Properties.Settings.Default.VisibilityProtected = exportProtected.Checked;
            Properties.Settings.Default.VisibilityPublic = exportPublic.Checked;
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Evaluate a preprocessor branch.
        /// </summary>
        /// <param name="parse">Condition</param>
        /// <param name="defineConstants">Constants that are true</param>
        bool ParseSimpleIf(string parse, string[] defineConstants) {
            int bracketPos;
            while ((bracketPos = parse.LastIndexOf('(')) != -1) {
                int endBracket = parse.IndexOf(')', bracketPos + 1);
                parse = parse.Substring(0, bracketPos) +
                    (ParseSimpleIf(parse.Substring(bracketPos + 1, endBracket - bracketPos - 1), defineConstants) ? _true : _false) +
                    parse.Substring(endBracket + 1);
            }
            string[] split = System.Text.RegularExpressions.Regex.Split(parse, splitRegex);
            int arrPos = 0, splitEnd = split.Length - 1;
            while (arrPos < splitEnd) {
                bool first = split[arrPos].Equals(_true) || Utils.ArrayContains(defineConstants, split[arrPos]),
                    second = split[arrPos + 2].Equals(_true) || Utils.ArrayContains(defineConstants, split[arrPos + 2]);
                split[arrPos + 2] = (split[arrPos + 1].Equals(_and) ? first && second : first || second) ? _true : _false;
                arrPos += 2;
            }
            return split[arrPos].Equals(_true);
        }

        /// <summary>
        /// Generate an element tree from code.
        /// </summary>
        /// <param name="code">Code</param>
        /// <param name="node">Root node</param>
        /// <param name="defines">Defined constants</param>
        void ParseBlock(string code, TreeNode node, string defines) {
            int codeLen = code.Length, lastEnding = 0, lastSlash = -2, lastEquals = -2, parenthesis = 0, depth = 0, lastRemovableDepth = 0;
            bool commentLine = false, inRemovableBlock = false, inString = false, preprocessorLine = false, preprocessorSkip = false,
                summaryLine = false, inTemplate = false, propertyArray = false;
            string summary = string.Empty;

            // Parse defines
            string[] defineConstants = defines.Split(';');
            int defineCount = defineConstants.Length;
            for (int i = 0; i < defineCount; ++i)
                defineConstants[i] = defineConstants[i].Trim();

            for (int i = 0; i < codeLen; ++i) {
                if (i != 0 && code[i - 1] != '\\') {
                    // Skip strings
                    if (!inString && code[i] == '"')
                        inString = true;
                    else if (inString) {
                        if (code[i] == '"')
                            inString = false;
                        else
                            continue;
                    // Skip characters
                    } else if (code[i] == '\'') {
                        if (code[i + 3] == '\'')
                            i += 4;
                        if (code[i + 2] == '\'')
                            i += 3;
                    }
                }
                // Actual parser
                switch (code[i]) {
                    case ',':
                    case ';':
                    case '{':
                    case '}':
                        if (!commentLine && !preprocessorSkip && parenthesis == 0) {
                            if (code[i] == ',' && inTemplate)
                                continue;
                            inTemplate = false;
                            bool instruction = code[i] == ';', block = code[i] == '{', closing = code[i] == '}';
                            if (block)
                                ++depth;
                            if (closing) {
                                --depth;
                                if (inRemovableBlock && depth < lastRemovableDepth) {
                                    inRemovableBlock = false;
                                    lastEnding = i;
                                    if (propertyArray) {
                                        ++lastEnding;
                                        propertyArray = false;
                                        break;
                                    }
                                }
                            }
                            if (inRemovableBlock)
                                continue;
                            string cutout = code.Substring(lastEnding, i - lastEnding).Trim();
                            lastEnding = i + 1;

                            // Remove multiline comments
                            int commentBlockStart;
                            while ((commentBlockStart = cutout.IndexOf(_commentBlockStart)) != -1) {
                                int commentEnd = cutout.IndexOf(_commentBlockEnd, commentBlockStart + 2) + 2;
                                if (commentEnd == 1)
                                    break;
                                cutout = cutout.Substring(0, commentBlockStart) + cutout.Substring(commentEnd);
                            }

                            if (cutout.StartsWith(_using))
                                break;

                            // Property array initialization
                            int nodes = node.Nodes.Count;
                            if (block && cutout.StartsWith(_equals) &&
                                nodes != 0 && ((ElementInfo)node.Nodes[nodes - 1].Tag).Kind == Element.Properties) {
                                lastRemovableDepth = depth;
                                inRemovableBlock = true;
                                propertyArray = true;
                                break;
                            }

                            if (cutout.EndsWith(_equals))
                                cutout = cutout.Remove(cutout.Length - 2, 1).TrimEnd();
                            int lambda = cutout.IndexOf(_lambda);
                            if (lambda >= 0)
                                cutout = cutout.Substring(0, lambda);

                            // Attributes
                            string attributes = string.Empty;
                            while (cutout.StartsWith(_indexStart)) {
                                int endPos = cutout.IndexOf(_indexEnd);
                                string between = cutout.Substring(1, endPos - 1);
                                if (attributes.Equals(string.Empty))
                                    attributes = between;
                                else
                                    attributes = string.Format("{0}, {1}", attributes, between);
                                cutout = cutout.Substring(endPos + 1).TrimStart();
                            }

                            // Default value
                            string defaultValue = string.Empty;
                            int eqPos = -1;
                            while ((eqPos = cutout.IndexOf('=', eqPos + 1)) != -1) {
                                if ((cutout.LastIndexOf('(', eqPos) == -1 || cutout.IndexOf(')', eqPos) == -1)) { // not a parameter
                                    defaultValue = cutout.Substring(eqPos + 1).TrimStart();
                                    cutout = cutout.Substring(0, eqPos).TrimEnd();
                                    break;
                                }
                            }

                            // Visibility
                            Visibility vis = Visibility.Default;
                            if (Utils.RemoveModifier(ref cutout, _private)) vis = Visibility.Private;
                            else if (Utils.RemoveModifier(ref cutout, _protected)) vis = Visibility.Protected;
                            else if (Utils.RemoveModifier(ref cutout, _internal)) vis = Visibility.Internal;
                            else if (Utils.RemoveModifier(ref cutout, _public)) vis = Visibility.Public;

                            // Modifiers
                            string modifiers = string.Empty;
                            Utils.RemoveModifier(ref cutout, _partial_);
                            Utils.MoveModifiers(ref cutout, ref modifiers, Constants.modifiers);

                            // Type
                            string type = string.Empty;
                            int spaceIndex = -1;
                            while ((spaceIndex = cutout.IndexOf(' ', spaceIndex + 1)) != -1) {
                                if ((spaceIndex == -1 || !cutout.Substring(0, spaceIndex).Equals(_delegate)) && // not just the delegate word
                                    // not in a template type
                                    (cutout.LastIndexOf('<', spaceIndex) == -1 || cutout.IndexOf('>', spaceIndex) == -1)) {
                                    type = cutout.Substring(0, spaceIndex);
                                    if (type.IndexOf('(') != -1)
                                        type = constructor;
                                    else
                                        cutout = cutout.Substring(spaceIndex).TrimStart();
                                    break;
                                }
                            }
                            Element kind = Element.Variables;
                            if (type.Equals(_class)) kind = Element.Classes;
                            else if (type.Equals(_interface)) kind = Element.Interfaces;
                            else if (type.Equals(_namespace)) kind = Element.Namespaces;
                            else if (type.Equals(_enum)) kind = Element.Enums;
                            else if (type.Equals(_struct)) kind = Element.Structs;
                            else if (lambda >= 0) kind = cutout.Contains('(') ? Element.Functions : Element.Properties;

                            // Extension
                            string extends = string.Empty;
                            int extensionIndex = cutout.IndexOf(':');
                            if (extensionIndex != -1) {
                                extends = cutout.Substring(extensionIndex + 1).TrimStart();
                                cutout = cutout.Substring(0, extensionIndex).TrimEnd();
                            }

                            // Default visibility
                            if (vis == Visibility.Default && kind != Element.Namespaces) {
                                if (node.Tag != null && (((ElementInfo)node.Tag).Kind == Element.Enums ||
                                    ((ElementInfo)node.Tag).Kind == Element.Interfaces))
                                    vis = Visibility.Public;
                                else if (kind == Element.Classes || kind == Element.Interfaces || kind == Element.Structs)
                                    vis = Visibility.Internal;
                                else
                                    vis = Visibility.Private;
                            }

                            // Multiple ; support
                            if (instruction && cutout.Equals(string.Empty)) {
                                summary = string.Empty;
                                break;
                            }

                            // Node handling
                            TreeNode newNode = Utils.GetNodeByText(node, cutout);
                            if (!cutout.Equals(string.Empty)) {
                                if (newNode == null) {
                                    Utils.MakeNodeName(newNode = node.Nodes.Add(Utils.RemoveParamNames(cutout)), vis);
                                    if (modifiers.Contains(_abstract))
                                        newNode.NodeFont = italic;
                                    else if (modifiers.Contains(_static))
                                        newNode.NodeFont = underline;
                                }
                                if (block) {
                                    node = newNode;
                                    if (kind == Element.Variables) {
                                        kind = cutout.IndexOf('(') != -1 ? Element.Functions : Element.Properties;
                                        lastRemovableDepth = depth;
                                        inRemovableBlock = true;
                                    }
                                }
                                if (newNode.Tag != null) {
                                    ElementInfo tag = (ElementInfo)newNode.Tag;
                                    tag.Summary += summary;
                                    newNode.Tag = tag;
                                } else if (type.Equals(string.Empty) && newNode.Parent.Nodes.Count > 1) { // "int a, b;" case, copy tags
                                    ElementInfo inherited = (ElementInfo)newNode.Parent.Nodes[newNode.Parent.Nodes.Count - 2].Tag;
                                    inherited.Name = cutout;
                                    inherited.Summary = summary;
                                    newNode.Tag = inherited;
                                } else
                                    newNode.Tag = new ElementInfo {
                                        Name = cutout, Attributes = attributes, DefaultValue = defaultValue, Extends = extends,
                                        Modifiers = modifiers.Trim(), Summary = summary, Type = type, Vis = vis, Kind = kind
                                    };
                            }
                            if (closing && node.Parent != null)
                                node = node.Parent;
                            summary = string.Empty;
                        }
                        break;
                    case '(':
                    case '[':
                        if (!inRemovableBlock && !commentLine && !preprocessorSkip)
                            ++parenthesis;
                        break;
                    case ')':
                    case ']':
                        if (lastEquals != i - 1 && !inRemovableBlock && !commentLine && !preprocessorSkip)
                            --parenthesis;
                        inTemplate = false;
                        break;
                    case '<':
                        inTemplate = true; // could be a simple "smaller than", but cancelled at all possibilities that break this assumption
                        break;
                    case '?':
                    case '>':
                        inTemplate = false;
                        break;
                    case '/':
                        if (!preprocessorSkip) {
                            if (!commentLine) {
                                if (lastSlash == i - 1)
                                    commentLine = true;
                                lastSlash = i;
                            } else if (commentLine && !summaryLine) {
                                if (lastSlash == i - 1)
                                    summaryLine = true;
                                lastSlash = i;
                            }
                        }
                        break;
                    case '=':
                        lastEquals = i;
                        break;
                    case '#':
                        commentLine = preprocessorLine = true;
                        break;
                    case '\n':
                        if (preprocessorLine) {
                            string line = code.Substring(lastEnding, i - lastEnding).Trim();
                            if (line.StartsWith(_define)) {
                                string defined = line.Substring(line.IndexOf(' ')).TrimStart();
                                Array.Resize(ref defineConstants, ++defineCount);
                                defineConstants[defineCount - 1] = defined;
                            } else if (line.StartsWith(_if) || line.StartsWith(_elif)) {
                                int commentPos;
                                if ((commentPos = line.IndexOf("//")) != -1)
                                    line = line.Substring(0, commentPos).TrimEnd();
                                preprocessorSkip = !ParseSimpleIf(line.Substring(line.IndexOf(' ')).TrimStart(), defineConstants);
                            } else if (line.StartsWith(_endif))
                                preprocessorSkip = false;
                            preprocessorLine = false;
                        }
                        if (commentLine || preprocessorSkip) {
                            commentLine = false;
                            lastEnding = i + 1;
                        }
                        if (summaryLine) {
                            summary += code.Substring(lastSlash + 1, i - lastSlash - 1).Trim() + '\n';
                            summaryLine = false;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Load code from a directory.
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="defines">Defined constants</param>
        void LoadFrom(string path, string defines) {
            if (!Directory.Exists(path))
                return;
            lastLoaded = path;
            currentDefines.Text = "Code loaded with: " + (defines.Equals(string.Empty) ? "-" : defines);
            string[] files = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
            sourceInfo.Nodes.Clear();
            TreeNode global = import = sourceInfo.Nodes.Add(path.Substring(path.LastIndexOf('\\') + 1));
            sourceInfo.BeginUpdate();
            foreach (string file in files) {
                string text = File.ReadAllText(file);
                ParseBlock(text, global, defines);
            }
            sourceInfo.EndUpdate();
        }

        /// <summary>
        /// Reload the code with new constants
        /// </summary>
        void ReloadConstants_Click(object sender, EventArgs e) {
            LoadFrom(lastLoaded, defines.Text);
        }

        /// <summary>
        /// Open the directory picker for loading code.
        /// </summary>
        void LoadSourceToolStripMenuItem_Click(object sender, EventArgs e) {
            if (folderDialog.ShowDialog() == DialogResult.OK) {
                LoadFrom(folderDialog.SelectedPath, defines.Text);
                if (Properties.Settings.Default.Recents.Contains(folderDialog.SelectedPath + '\n'))
                    LoadRecent(folderDialog.SelectedPath);
                else {
                    string newRecents = folderDialog.SelectedPath + '\n' + Properties.Settings.Default.Recents;
                    while (newRecents.Count(c => c == '\n') > 10)
                        newRecents = newRecents.Substring(0, newRecents.LastIndexOf('\n'));
                    Properties.Settings.Default.Recents = newRecents;
                    Properties.Settings.Default.Save();
                    LoadRecents();
                }
            }
        }

        /// <summary>
        /// Display the selected code element's properties.
        /// </summary>
        private void SourceInfo_AfterSelect(object sender, TreeViewEventArgs e) {
            string info = string.Empty;
            TreeNode node = sourceInfo.SelectedNode;
            if (node != null && node.Tag != null) {
                ElementInfo tag = (ElementInfo)node.Tag;
                Utils.AppendIfExists(ref info, "Attributes", tag.Attributes);
                Utils.AppendIfExists(ref info, "Visibility", tag.Vis.ToString().ToLower());
                Utils.AppendIfExists(ref info, "Modifiers", tag.Modifiers);
                Utils.AppendIfExists(ref info, "Type", tag.Type);
                Utils.AppendIfExists(ref info, "Default value", tag.DefaultValue);
                Utils.AppendIfExists(ref info, "Extends", tag.Extends);
                if (tag.Summary.Length != 0)
                    info = string.Format("{0}\n\n{1}", info, Utils.QuickSummary(tag.Summary));
            }
            infoLabel.Text = info;
        }

        /// <summary>
        /// Set the exportability of the selected and all child nodes based on user preferences.
        /// </summary>
        /// <param name="node">Root node</param>
        void SetExportability(TreeNode node) {
            foreach (TreeNode child in node.Nodes) {
                ElementInfo tag = ((ElementInfo)child.Tag);
                if (tag.Vis == Visibility.Default || (tag.Vis == Visibility.Public && exportPublic.Checked) ||
                    (tag.Vis == Visibility.Internal && exportInternal.Checked) || (tag.Vis == Visibility.Protected && exportProtected.Checked) ||
                    (tag.Vis == Visibility.Private && exportPrivate.Checked))
                    tag.Exportable = true;
                if (tag.Exportable) {
                    child.Tag = tag;
                    if (tag.Kind == Element.Enums) {
                        if (expandEnums.Checked)
                            SetExportability(child);
                    } else if (tag.Kind == Element.Structs) {
                        if (expandStructs.Checked)
                            SetExportability(child);
                    } else
                        SetExportability(child);
                }
            }
        }

        /// <summary>
        /// Start documentation generation process.
        /// </summary>
        void GenerateButton_Click(object sender, EventArgs e) {
            if (import == null)
                return;

            SetExportability(import);

            if (folderDialog.ShowDialog() == DialogResult.OK) {
                DirectoryInfo dir = new DirectoryInfo(folderDialog.SelectedPath);
                if ((dir.GetDirectories().Length == 0 && dir.GetDirectories().Length == 0) ||
                    MessageBox.Show(string.Format("The folder ({0}) is not empty. Continue generation anyway?", folderDialog.SelectedPath),
                        "Warning", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    Design.Extension = extension.Text.Equals(string.Empty) ? "html" : extension.Text;
                    Design.ExportAttributes = exportAttributes.Checked;
                    Design.GenerateDocumentation(import, folderDialog.SelectedPath);
                    Process.Start(folderDialog.SelectedPath);
                    if (phpFillers.Checked && !extension.Text.Equals("php"))
                        Utils.FillWithPHP(new DirectoryInfo(folderDialog.SelectedPath));
                }
            }
        }

        /// <summary>
        /// Show "About".
        /// </summary>
        void AboutToolStripMenuItem_Click(object sender, EventArgs e) {
            MessageBox.Show(string.Format("Doc# v{0} by VoidX\n{1}",
                FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion, "http://en.sbence.hu/"), "About");
        }

        const string splitRegex = @"\s+", _commentBlockStart = "/*", _commentBlockEnd = "*/", _indexStart = "[", _indexEnd = "]";
        const string _public = "public", _internal = "internal", _protected = "protected", _private = "private";
        const string _abstract = "abstract", _partial_ = "partial ", _static = "static", _using = "using";
        const string _class = "class", _enum = "enum", _interface = "interface", _namespace = "namespace", _struct = "struct";
        const string constructor = "Constructor", _delegate = "delegate";
        const string _true = "true", _false = "false", _and = "&&", _equals = "=", _lambda = "=>";
        const string _define = "#define", _if = "#if", _elif = "#elif", _endif = "#endif";
    }
}