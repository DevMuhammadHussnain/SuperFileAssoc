/*
╔══════════════════════════════════════════════════════════════════════════════╗
║         SuperFileAssoc — Advanced File & Folder Association Utility          ║
║         (c) 2024-2026 Muhammad Hussnain, MiSeSys, Contributors               ║
║         Target: Windows 10/11  |  .NET 8+  |  VS 2022/2026                   ║
╚══════════════════════════════════════════════════════════════════════════════╝

Features (40+):
  • Per-extension : icon, displayname, description, perceivedtype, openwith,
                    context verbs, content type, always-show-ext, NeverShowExt
  • Per-folder    : desktop.ini icon, InfoTip, LocalizedName, custom template,
                    reset, read/write protection toggle
  • Per-file      : shortcut-hack icon, real shortcut icon update, hide/unhide
  • Bulk ops      : multi-target, multi-extension, recursive, dry-run, filter
  • Registry      : backup/restore (full reg export), import/export (JSON)
  • Queries       : queryfile, queryext, queryfolder, listext, listfiles,
                    listfolders, listverbs
  • Extra hacks   : thumbnail handler, content-type, always-show-ext,
                    NeverShowExt, custom shell column, open-with list prune,
                    sendto verb, new-menu template, undo stack

Build requirements:
  • Project type  : C# Console App (.NET 8 or later)
  • NuGet         : Newtonsoft.Json (for import/export JSON)
  • Platform      : x64 or AnyCPU  — must run as Administrator for registry ops
  • Add to .csproj inside <PropertyGroup>:
        <PlatformTarget>AnyCPU</PlatformTarget>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>

Usage examples (run as Admin):
  SuperFileAssoc -exticon .log "C:\Icons\log.ico"
  SuperFileAssoc -foldericon "C:\MyFolder" "C:\Icons\folder.ico"
  SuperFileAssoc -contextverb OpenNote "notepad.exe \"%1\"" "Edit in Notepad" -inextension .txt
  SuperFileAssoc -openwith .mp3 "C:\Program Files\VLC\vlc.exe"
  SuperFileAssoc -bulk "C:\Projects" -inextensions .cs,.vb -recurse -dryrun
  SuperFileAssoc -backup "C:\backup\assoc_backup.json"
  SuperFileAssoc -restore "C:\backup\assoc_backup.json"
  SuperFileAssoc -listext
  SuperFileAssoc -queryext .pdf
  SuperFileAssoc -resetfoldericon "C:\MyFolder"
*/

using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Xml;

namespace SuperFileAssoc
{
    // ═══════════════════════════ DATA MODELS ═══════════════════════════════

    public class ActionResult
    {
        public bool Success { get; set; }
        public string Details { get; set; } = "";
        public override string ToString() => $"[{(Success ? "OK" : "FAIL")}] {Details}";
    }

    public class ExtensionInfo
    {
        public string Extension { get; set; } = "";
        public string ProgId { get; set; } = "";
        public string Icon { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string PerceivedType { get; set; } = "";
        public string OpenWith { get; set; } = "";
        public List<VerbInfo> Verbs { get; set; } = new();
    }

    public class VerbInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Command { get; set; } = "";
    }

    public class FolderInfo
    {
        public string Path { get; set; } = "";
        public string Icon { get; set; } = "";
        public string InfoTip { get; set; } = "";
        public string LocalizedName { get; set; } = "";
    }

    public class BackupData
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string MachineName { get; set; } = Environment.MachineName;
        public List<ExtensionInfo> Extensions { get; set; } = new();
        public List<FolderInfo> Folders { get; set; } = new();
    }

    public enum Mode { Extension, Folder, File, Hybrid, Bulk }

    // ═══════════════════════════ UNDO STACK ════════════════════════════════

    public class UndoAction
    {
        public string Description { get; set; } = "";
        public Action Undo { get; set; } = () => { };
    }

    // ═══════════════════════════ MAIN PROGRAM ══════════════════════════════

    [SupportedOSPlatform("windows")]
    internal class Program
    {
        // ── P/Invoke ──────────────────────────────────────────────────────
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

        // ── State ─────────────────────────────────────────────────────────
        static bool dryrun = false, verbose = false, quiet = false, recursive = false;
        static Mode mode = Mode.Hybrid;

        static string? folderIcon, fileIcon, extensionIcon, shortcutIcon;
        static string? folderTarget, fileTarget, extensionTarget;
        static string? desktopIniTemplate, contextVerbCmd, contextVerbDisp, contextVerbName;
        static string? exportPath, importPath, openWithProgram, displayName, description;
        static string? perceivedType, contentType, thumbnailHandler, backupPath;
        static string? deleteVerbName, sendToName, newMenuTemplate;
        static bool alwaysShowExt = false, neverShowExt = false, hideFile = false, unhideFile = false;
        static bool pruneOpenWith = false, showInExplorer = false;

        static List<string> extensions = new();
        static List<string> files = new();
        static List<string> folders = new();
        static List<string> targets = new();
        static List<string> output = new();
        static List<string> flags = new();
        static Stack<UndoAction> undoStack = new();

        // ═══════════════════════════ ENTRY POINT ═══════════════════════════

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            PrintBanner();

            if (args.Length == 0) { ShowHelp(); return; }

            ParseArguments(args);

            if (flags.Contains("-help") || flags.Contains("-h") || flags.Contains("/?")) { ShowHelp(); return; }
            if (flags.Contains("-about")) { ShowAbout(); return; }

            // Admin check for registry ops
            if (!IsAdmin() && NeedsAdmin())
            {
                Warn("Some operations require Administrator privileges. Run as Admin for registry/system changes.");
            }

            // ── Per-Extension ──────────────────────────────────────────────
            if (flags.Contains("-exticon") && extensionTarget != null && extensionIcon != null)
                Log(SetExtensionIcon(extensionTarget, extensionIcon));

            if (flags.Contains("-deleteexticon") && extensionTarget != null)
                Log(DeleteExtensionIcon(extensionTarget));

            if (flags.Contains("-displayname") && extensionTarget != null && displayName != null)
                Log(SetDisplayName(extensionTarget, displayName));

            if (flags.Contains("-description") && extensionTarget != null && description != null)
                Log(SetDescription(extensionTarget, description));

            if (flags.Contains("-perceivedtype") && extensionTarget != null && perceivedType != null)
                Log(SetPerceivedType(extensionTarget, perceivedType));

            if (flags.Contains("-contenttype") && extensionTarget != null && contentType != null)
                Log(SetContentType(extensionTarget, contentType));

            if (flags.Contains("-openwith") && extensionTarget != null && openWithProgram != null)
                Log(SetOpenWith(extensionTarget, openWithProgram));

            if (flags.Contains("-alwaysshowext") && extensionTarget != null)
                Log(SetAlwaysShowExt(extensionTarget, true));

            if (flags.Contains("-nevershowext") && extensionTarget != null)
                Log(SetNeverShowExt(extensionTarget, true));

            if (flags.Contains("-contextverb") && contextVerbName != null && contextVerbCmd != null && contextVerbDisp != null)
                Log(SetContextVerb(extensionTarget, contextVerbName, contextVerbCmd, contextVerbDisp));

            if (flags.Contains("-deleteverb") && deleteVerbName != null)
                Log(DeleteVerb(extensionTarget, deleteVerbName));

            if (flags.Contains("-addtosendto") && extensionTarget != null && sendToName != null && openWithProgram != null)
                Log(AddToSendTo(sendToName, openWithProgram));

            if (flags.Contains("-newmenutemplate") && extensionTarget != null && newMenuTemplate != null)
                Log(SetNewMenuTemplate(extensionTarget, newMenuTemplate));

            if (flags.Contains("-thumbnailhandler") && extensionTarget != null && thumbnailHandler != null)
                Log(SetThumbnailHandler(extensionTarget, thumbnailHandler));

            if (flags.Contains("-pruneopenwith") && extensionTarget != null)
                Log(PruneOpenWithList(extensionTarget));

            // ── Per-Folder ─────────────────────────────────────────────────
            if (flags.Contains("-foldericon") && folderTarget != null && folderIcon != null)
                Log(SetFolderIcon(folderTarget, folderIcon));

            if (flags.Contains("-resetfoldericon") && folderTarget != null)
                Log(SetFolderIcon(folderTarget, null, true));

            if (flags.Contains("-folderinfotip") && folderTarget != null && description != null)
                Log(SetFolderInfoTip(folderTarget, description));

            if (flags.Contains("-folderlocalizedname") && folderTarget != null && displayName != null)
                Log(SetFolderLocalizedName(folderTarget, displayName));

            if (flags.Contains("-desktopini") && folderTarget != null && desktopIniTemplate != null)
                Log(ApplyDesktopIni(folderTarget, desktopIniTemplate));

            if (flags.Contains("-resetdesktopini") && folderTarget != null)
                Log(ResetDesktopIni(folderTarget));

            if (flags.Contains("-folderprotect") && folderTarget != null)
                Log(SetFolderProtection(folderTarget, true));

            if (flags.Contains("-folderunprotect") && folderTarget != null)
                Log(SetFolderProtection(folderTarget, false));

            if (flags.Contains("-contextverb") && folderTarget != null && contextVerbName != null && contextVerbCmd != null && contextVerbDisp != null && extensionTarget == null)
                Log(SetFolderContextVerb(contextVerbName, contextVerbCmd, contextVerbDisp));

            // ── Per-File ───────────────────────────────────────────────────
            if (flags.Contains("-fileicon") && fileTarget != null && fileIcon != null)
                Log(SetFileIcon(fileTarget, fileIcon));

            if (flags.Contains("-shortcuticon") && fileTarget != null && shortcutIcon != null)
                Log(SetFileShortcutIcon(fileTarget, shortcutIcon));

            if (flags.Contains("-hidefile") && fileTarget != null)
                Log(SetFileHidden(fileTarget, true));

            if (flags.Contains("-unhidefile") && fileTarget != null)
                Log(SetFileHidden(fileTarget, false));

            if (flags.Contains("-createshortcut") && fileTarget != null && shortcutIcon != null)
                Log(CreateShortcut(fileTarget, shortcutIcon));

            // ── Query / Info ───────────────────────────────────────────────
            if (flags.Contains("-queryext") && extensionTarget != null)
                QueryExtension(extensionTarget);

            if (flags.Contains("-queryfolder") && folderTarget != null)
                QueryFolder(folderTarget);

            if (flags.Contains("-queryfile") && fileTarget != null)
                QueryFile(fileTarget);

            if (flags.Contains("-listext"))
                ListAllExtensions();

            if (flags.Contains("-listfiles") && targets.Count > 0)
                ListAllFiles(targets, extensions);

            if (flags.Contains("-listfolders") && targets.Count > 0)
                ListAllFolders(targets);

            if (flags.Contains("-listverbs"))
                ListVerbs(extensionTarget);

            // ── Bulk ───────────────────────────────────────────────────────
            if (flags.Contains("-bulk") && targets.Count > 0)
                BulkAction(targets, extensions, folders, files, recursive);

            // ── Backup / Restore ───────────────────────────────────────────
            if (flags.Contains("-backup") && backupPath != null)
                Log(BackupAssociations(backupPath));

            if (flags.Contains("-restore") && backupPath != null)
                Log(RestoreAssociations(backupPath));

            if (flags.Contains("-export") && exportPath != null)
                Log(ExportData(exportPath));

            if (flags.Contains("-import") && importPath != null)
                Log(ImportData(importPath));

            // ── Registry Export (reg file) ─────────────────────────────────
            if (flags.Contains("-regexport") && exportPath != null)
                Log(RegExport(exportPath));

            if (flags.Contains("-regimport") && importPath != null)
                Log(RegImport(importPath));

            // ── Undo ───────────────────────────────────────────────────────
            if (flags.Contains("-undo"))
                PerformUndo();

            // ── Refresh ────────────────────────────────────────────────────
            if (flags.Contains("-refresh"))
                RefreshExplorer();

            // ── Output ─────────────────────────────────────────────────────
            foreach (var line in output)
                Console.WriteLine(line);

            if (!quiet)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nDone.");
                Console.ResetColor();
            }
        }

        // ═══════════════════════════ ARGUMENT PARSER ═══════════════════════

        static void ParseArguments(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string aLow = a.ToLowerInvariant();

                // Collect flags
                if (aLow.StartsWith("-") || aLow.StartsWith("/"))
                {
                    string flag = aLow.StartsWith("/") ? "-" + aLow[1..] : aLow;
                    if (!flags.Contains(flag)) flags.Add(flag);
                }

                string Next(ref int idx) => (idx + 1 < args.Length) ? args[++idx] : throw new ArgumentException($"Missing value after '{args[idx]}'");

                switch (aLow)
                {
                    case "-dryrun": dryrun = true; break;
                    case "-quiet": quiet = true; break;
                    case "-verbose": verbose = true; break;
                    case "-recurse":
                    case "-r": recursive = true; break;
                    case "-alwaysshowext": alwaysShowExt = true; break;
                    case "-nevershowext": neverShowExt = true; break;
                    case "-hidefile": hideFile = true; break;
                    case "-unhidefile": unhideFile = true; break;
                    case "-pruneopenwith": pruneOpenWith = true; break;

                    // Targets
                    case "-inextension": extensionTarget = NormExt(Next(ref i)); break;
                    case "-inextensions": extensions.AddRange(Next(ref i).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(NormExt)); break;
                    case "-infolder": folderTarget = Next(ref i); break;
                    case "-infolders": folders.AddRange(Next(ref i).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)); break;
                    case "-infile": fileTarget = Next(ref i); break;
                    case "-infiles": files.AddRange(Next(ref i).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)); break;
                    case "-bulk":
                        targets.AddRange(Next(ref i).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
                        flags.Add("-bulk");
                        break;

                    // Per-extension ops
                    case "-exticon":
                        extensionTarget = NormExt(Next(ref i));
                        extensionIcon = Next(ref i);
                        break;

                    case "-openwith":
                        if (extensionTarget == null) extensionTarget = NormExt(Next(ref i));
                        openWithProgram = Next(ref i);
                        break;

                    case "-displayname":
                        displayName = Next(ref i);
                        break;

                    case "-description":
                        description = Next(ref i);
                        break;

                    case "-perceivedtype":
                        perceivedType = Next(ref i);
                        break;

                    case "-contenttype":
                        contentType = Next(ref i);
                        break;

                    case "-contextverb":
                        contextVerbName = Next(ref i);
                        contextVerbCmd = Next(ref i);
                        contextVerbDisp = Next(ref i);
                        break;

                    case "-deleteverb":
                        deleteVerbName = Next(ref i);
                        break;

                    case "-thumbnailhandler":
                        thumbnailHandler = Next(ref i);
                        break;

                    case "-newmenutemplate":
                        newMenuTemplate = Next(ref i);
                        break;

                    case "-addtosendto":
                        sendToName = Next(ref i);
                        openWithProgram = Next(ref i);
                        break;

                    // Per-folder ops
                    case "-foldericon":
                        folderTarget = Next(ref i);
                        folderIcon = Next(ref i);
                        break;

                    case "-resetfoldericon":
                        folderTarget = Next(ref i);
                        break;

                    case "-folderinfotip":
                        folderTarget = Next(ref i);
                        description = Next(ref i);
                        break;

                    case "-folderlocalizedname":
                        folderTarget = Next(ref i);
                        displayName = Next(ref i);
                        break;

                    case "-desktopini":
                        folderTarget = Next(ref i);
                        desktopIniTemplate = Next(ref i);
                        break;

                    case "-resetdesktopini":
                        folderTarget = Next(ref i);
                        break;

                    case "-folderprotect":
                    case "-folderunprotect":
                        folderTarget = Next(ref i);
                        break;

                    // Per-file ops
                    case "-fileicon":
                        fileTarget = Next(ref i);
                        fileIcon = Next(ref i);
                        break;

                    case "-shortcuticon":
                        fileTarget = Next(ref i);
                        shortcutIcon = Next(ref i);
                        break;

                    case "-createshortcut":
                        fileTarget = Next(ref i);
                        shortcutIcon = Next(ref i);
                        break;

                    // Query ops
                    case "-queryext": extensionTarget = NormExt(Next(ref i)); break;
                    case "-queryfolder": folderTarget = Next(ref i); break;
                    case "-queryfile": fileTarget = Next(ref i); break;
                    case "-listverbs":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            extensionTarget = NormExt(Next(ref i));
                        break;
                    case "-listfiles":
                        targets.AddRange(Next(ref i).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
                        break;
                    case "-listfolders":
                        targets.AddRange(Next(ref i).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
                        break;

                    // Backup / restore / export / import
                    case "-backup": backupPath = Next(ref i); break;
                    case "-restore": backupPath = Next(ref i); break;
                    case "-export": exportPath = Next(ref i); break;
                    case "-import": importPath = Next(ref i); break;
                    case "-regexport": exportPath = Next(ref i); break;
                    case "-regimport": importPath = Next(ref i); break;
                }
            }
        }

        // ═══════════════════════ PER-EXTENSION FEATURES ════════════════════

        // ── Set extension icon ─────────────────────────────────────────────
        static ActionResult SetExtensionIcon(string ext, string icon)
        {
            if (dryrun) return DryRun($"SetExtensionIcon({ext}, {icon})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                using var key = Registry.ClassesRoot.CreateSubKey($@"{progId}\DefaultIcon");
                string? old = key.GetValue("") as string;
                key.SetValue("", icon);
                PushUndo($"Undo icon for {ext}", () =>
                {
                    using var k = Registry.ClassesRoot.CreateSubKey($@"{progId}\DefaultIcon");
                    if (old != null) k.SetValue("", old); else k.DeleteValue("", false);
                });
                RefreshExplorer();
                return OK($"Extension '{ext}' icon → '{icon}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Delete extension icon ──────────────────────────────────────────
        static ActionResult DeleteExtensionIcon(string ext)
        {
            if (dryrun) return DryRun($"DeleteExtensionIcon({ext})");
            try
            {
                ext = NormExt(ext);
                string progId = GetProgId(ext);
                Registry.ClassesRoot.DeleteSubKeyTree($@"{progId}\DefaultIcon", false);
                RefreshExplorer();
                return OK($"Icon removed for '{ext}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set display name ───────────────────────────────────────────────
        static ActionResult SetDisplayName(string ext, string name)
        {
            if (dryrun) return DryRun($"SetDisplayName({ext}, {name})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                using var key = Registry.ClassesRoot.CreateSubKey(progId);
                key.SetValue("", name);
                return OK($"DisplayName for '{ext}' → '{name}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set description (FriendlyTypeName) ────────────────────────────
        static ActionResult SetDescription(string ext, string desc)
        {
            if (dryrun) return DryRun($"SetDescription({ext}, {desc})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                using var key = Registry.ClassesRoot.CreateSubKey(progId);
                key.SetValue("FriendlyTypeName", desc);
                return OK($"Description for '{ext}' → '{desc}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set perceived type ────────────────────────────────────────────
        static ActionResult SetPerceivedType(string ext, string type)
        {
            if (dryrun) return DryRun($"SetPerceivedType({ext}, {type})");
            try
            {
                ext = NormExt(ext);
                using var key = Registry.ClassesRoot.CreateSubKey(ext);
                key.SetValue("PerceivedType", type);
                return OK($"PerceivedType for '{ext}' → '{type}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set content type ──────────────────────────────────────────────
        static ActionResult SetContentType(string ext, string ct)
        {
            if (dryrun) return DryRun($"SetContentType({ext}, {ct})");
            try
            {
                ext = NormExt(ext);
                using var key = Registry.ClassesRoot.CreateSubKey(ext);
                key.SetValue("Content Type", ct);
                // Mirror in MIME database
                using var mimeKey = Registry.ClassesRoot.CreateSubKey($@"MIME\Database\Content Type\{ct}");
                mimeKey.SetValue("Extension", ext);
                return OK($"Content-Type for '{ext}' → '{ct}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set AlwaysShowExt ─────────────────────────────────────────────
        static ActionResult SetAlwaysShowExt(string ext, bool always)
        {
            if (dryrun) return DryRun($"SetAlwaysShowExt({ext}, {always})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                using var key = Registry.ClassesRoot.CreateSubKey(progId);
                if (always) key.SetValue("AlwaysShowExt", "");
                else key.DeleteValue("AlwaysShowExt", false);
                RefreshExplorer();
                return OK($"AlwaysShowExt for '{ext}' → {always}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set NeverShowExt ──────────────────────────────────────────────
        static ActionResult SetNeverShowExt(string ext, bool never)
        {
            if (dryrun) return DryRun($"SetNeverShowExt({ext}, {never})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                using var key = Registry.ClassesRoot.CreateSubKey(progId);
                if (never) key.SetValue("NeverShowExt", "");
                else key.DeleteValue("NeverShowExt", false);
                RefreshExplorer();
                return OK($"NeverShowExt for '{ext}' → {never}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set Open-With (default program) ───────────────────────────────
        static ActionResult SetOpenWith(string ext, string program)
        {
            if (dryrun) return DryRun($"SetOpenWith({ext}, {program})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);

                // Set default program via shell\open\command
                using var cmdKey = Registry.ClassesRoot.CreateSubKey($@"{progId}\shell\open\command");
                cmdKey.SetValue("", $"\"{program}\" \"%1\"");

                // Also register in OpenWithList
                string exeName = Path.GetFileName(program);
                using var owlKey = Registry.ClassesRoot.CreateSubKey($@"{ext}\OpenWithList\{exeName}");
                owlKey.SetValue("", "");

                // Register in OpenWithProgIds
                using var owpKey = Registry.ClassesRoot.CreateSubKey($@"{ext}\OpenWithProgids");
                owpKey.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.Binary);

                RefreshExplorer();
                return OK($"OpenWith for '{ext}' → '{program}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set context verb (for extension or folder) ────────────────────
        static ActionResult SetContextVerb(string? ext, string name, string cmd, string disp)
        {
            if (dryrun) return DryRun($"SetContextVerb({ext ?? "folder"}, {name}, {cmd}, {disp})");
            try
            {
                string root;
                if (ext != null)
                {
                    ext = NormExt(ext);
                    root = GetOrCreateProgId(ext);
                }
                else
                {
                    root = "Directory";
                }

                string verbPath = $@"{root}\shell\{name}";
                using (var vk = Registry.ClassesRoot.CreateSubKey(verbPath))
                    vk.SetValue("", disp);
                using (var ck = Registry.ClassesRoot.CreateSubKey($@"{verbPath}\command"))
                    ck.SetValue("", cmd.Contains("%1") ? cmd : $"{cmd} \"%1\"");

                RefreshExplorer();
                return OK($"Verb '{name}' ('{disp}') added to {(ext ?? "folders")} → '{cmd}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Delete context verb ───────────────────────────────────────────
        static ActionResult DeleteVerb(string? ext, string name)
        {
            if (dryrun) return DryRun($"DeleteVerb({ext ?? "folder"}, {name})");
            try
            {
                string root = ext != null ? GetProgId(NormExt(ext)) : "Directory";
                Registry.ClassesRoot.DeleteSubKeyTree($@"{root}\shell\{name}", false);
                RefreshExplorer();
                return OK($"Verb '{name}' removed from {(ext ?? "folders")}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set folder context verb ───────────────────────────────────────
        static ActionResult SetFolderContextVerb(string name, string cmd, string disp)
            => SetContextVerb(null, name, cmd, disp);

        // ── Add to SendTo ─────────────────────────────────────────────────
        static ActionResult AddToSendTo(string name, string program)
        {
            if (dryrun) return DryRun($"AddToSendTo({name}, {program})");
            try
            {
                string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
                string lnk = Path.Combine(sendTo, name + ".lnk");
                CreateLnk(lnk, program, program, null);
                return OK($"Added '{name}' to SendTo → '{program}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set New Menu template ─────────────────────────────────────────
        static ActionResult SetNewMenuTemplate(string ext, string templateFile)
        {
            if (dryrun) return DryRun($"SetNewMenuTemplate({ext}, {templateFile})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                string shellNewPath = $@"{ext}\ShellNew";
                using var key = Registry.ClassesRoot.CreateSubKey(shellNewPath);
                if (File.Exists(templateFile))
                {
                    string dest = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        $"ShellNew{ext.Replace(".", "_")}{Path.GetExtension(templateFile)}");
                    if (!dryrun) File.Copy(templateFile, dest, true);
                    key.SetValue("FileName", dest);
                }
                else
                {
                    key.SetValue("NullFile", "");
                }
                RefreshExplorer();
                return OK($"New-menu template for '{ext}' set");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set thumbnail handler ─────────────────────────────────────────
        static ActionResult SetThumbnailHandler(string ext, string clsid)
        {
            if (dryrun) return DryRun($"SetThumbnailHandler({ext}, {clsid})");
            try
            {
                ext = NormExt(ext);
                string progId = GetOrCreateProgId(ext);
                using var key = Registry.ClassesRoot.CreateSubKey($@"{progId}\shellex\{{e357fccd-a995-4576-b01f-234630154e96}}");
                key.SetValue("", clsid);
                return OK($"ThumbnailHandler for '{ext}' → '{clsid}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Prune Open-With list ──────────────────────────────────────────
        static ActionResult PruneOpenWithList(string ext)
        {
            if (dryrun) return DryRun($"PruneOpenWithList({ext})");
            try
            {
                ext = NormExt(ext);
                using var owlKey = Registry.ClassesRoot.OpenSubKey($@"{ext}\OpenWithList", true);
                if (owlKey != null)
                {
                    foreach (var sub in owlKey.GetSubKeyNames())
                    {
                        string exe = sub;
                        // Remove entries where exe no longer exists on PATH
                        bool found = Environment.GetEnvironmentVariable("PATH")!
                            .Split(';')
                            .Any(p => File.Exists(Path.Combine(p, exe)));
                        if (!found)
                        {
                            owlKey.DeleteSubKeyTree(sub, false);
                            output.Add($"  Removed stale OpenWith: {sub}");
                        }
                    }
                }
                return OK($"OpenWithList for '{ext}' pruned");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ═══════════════════════ PER-FOLDER FEATURES ═══════════════════════

        // ── Set folder icon (desktop.ini) ─────────────────────────────────
        static ActionResult SetFolderIcon(string folder, string? icon, bool reset = false)
        {
            if (dryrun) return DryRun(reset ? $"ResetFolderIcon({folder})" : $"SetFolderIcon({folder}, {icon})");
            if (!Directory.Exists(folder)) return Fail($"Folder not found: {folder}");
            try
            {
                string ini = Path.Combine(folder, "desktop.ini");
                if (reset)
                {
                    if (File.Exists(ini))
                    {
                        // Remove attributes before delete
                        File.SetAttributes(ini, FileAttributes.Normal);
                        File.Delete(ini);
                    }
                    RemoveAttr(folder, FileAttributes.System | FileAttributes.ReadOnly);
                    RefreshExplorer();
                    return OK($"Folder icon reset: '{folder}'");
                }

                // Read existing desktop.ini to preserve other settings
                var ini_content = ParseDesktopIni(ini);
                ini_content["[.ShellClassInfo]"]["IconResource"] = $"{icon},0";
                ini_content["[.ShellClassInfo]"]["IconIndex"] = "0";
                WriteDesktopIni(folder, ini, ini_content);

                PushUndo($"Undo folder icon '{folder}'", () => SetFolderIcon(folder, null, true));
                RefreshExplorer();
                return OK($"Folder '{folder}' → icon '{icon}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set folder InfoTip ────────────────────────────────────────────
        static ActionResult SetFolderInfoTip(string folder, string tip)
        {
            if (dryrun) return DryRun($"SetFolderInfoTip({folder}, {tip})");
            if (!Directory.Exists(folder)) return Fail($"Folder not found: {folder}");
            try
            {
                string ini = Path.Combine(folder, "desktop.ini");
                var content = ParseDesktopIni(ini);
                content["[.ShellClassInfo]"]["InfoTip"] = tip;
                WriteDesktopIni(folder, ini, content);
                return OK($"InfoTip for '{folder}' → '{tip}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set folder LocalizedName ──────────────────────────────────────
        static ActionResult SetFolderLocalizedName(string folder, string name)
        {
            if (dryrun) return DryRun($"SetFolderLocalizedName({folder}, {name})");
            if (!Directory.Exists(folder)) return Fail($"Folder not found: {folder}");
            try
            {
                string ini = Path.Combine(folder, "desktop.ini");
                var content = ParseDesktopIni(ini);
                content["[.ShellClassInfo]"]["LocalizedResourceName"] = name;
                WriteDesktopIni(folder, ini, content);
                RefreshExplorer();
                return OK($"LocalizedName for '{folder}' → '{name}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Apply custom desktop.ini template ─────────────────────────────
        static ActionResult ApplyDesktopIni(string folder, string templatePathOrContent)
        {
            if (dryrun) return DryRun($"ApplyDesktopIni({folder})");
            if (!Directory.Exists(folder)) return Fail($"Folder not found: {folder}");
            try
            {
                string ini = Path.Combine(folder, "desktop.ini");
                string content = File.Exists(templatePathOrContent)
                    ? File.ReadAllText(templatePathOrContent)
                    : templatePathOrContent;
                if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                File.WriteAllText(ini, content, Encoding.Unicode);
                SetAttr(folder, FileAttributes.System);
                SetAttr(ini, FileAttributes.Hidden | FileAttributes.System);
                RefreshExplorer();
                return OK($"Custom desktop.ini applied to '{folder}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Reset desktop.ini ─────────────────────────────────────────────
        static ActionResult ResetDesktopIni(string folder)
        {
            if (dryrun) return DryRun($"ResetDesktopIni({folder})");
            if (!Directory.Exists(folder)) return Fail($"Folder not found: {folder}");
            try
            {
                string ini = Path.Combine(folder, "desktop.ini");
                if (File.Exists(ini)) { File.SetAttributes(ini, FileAttributes.Normal); File.Delete(ini); }
                RemoveAttr(folder, FileAttributes.System | FileAttributes.ReadOnly);
                RefreshExplorer();
                return OK($"desktop.ini removed from '{folder}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set folder read/write protection ──────────────────────────────
        static ActionResult SetFolderProtection(string folder, bool protect)
        {
            if (dryrun) return DryRun($"SetFolderProtection({folder}, {protect})");
            if (!Directory.Exists(folder)) return Fail($"Folder not found: {folder}");
            try
            {
                var di = new DirectoryInfo(folder);
                if (protect) di.Attributes |= FileAttributes.ReadOnly;
                else di.Attributes &= ~FileAttributes.ReadOnly;
                return OK($"Folder '{folder}' {(protect ? "protected (ReadOnly)" : "unprotected")}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ═══════════════════════ PER-FILE FEATURES ═════════════════════════

        // ── Set file icon via shortcut hack ───────────────────────────────
        static ActionResult SetFileIcon(string file, string icon)
        {
            if (dryrun) return DryRun($"SetFileIcon({file}, {icon})");
            if (!File.Exists(file)) return Fail($"File not found: {file}");
            try
            {
                string lnk = Path.ChangeExtension(file, ".lnk");
                CreateLnk(lnk, file, icon, null);
                // Hide original so the shortcut is what's visible
                SetAttr(file, FileAttributes.Hidden);
                return OK($"Shortcut with icon '{icon}' created; original hidden: '{file}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Set existing shortcut icon ────────────────────────────────────
        static ActionResult SetFileShortcutIcon(string lnkFile, string icon)
        {
            if (dryrun) return DryRun($"SetFileShortcutIcon({lnkFile}, {icon})");
            if (!File.Exists(lnkFile)) return Fail($"Shortcut not found: {lnkFile}");
            try
            {
                // Update in-place via IShellLink COM
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                var sc = shell.CreateShortcut(lnkFile);
                sc.IconLocation = icon;
                sc.Save();
                return OK($"Shortcut icon updated: '{lnkFile}' → '{icon}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Create shortcut ───────────────────────────────────────────────
        static ActionResult CreateShortcut(string target, string icon)
        {
            if (dryrun) return DryRun($"CreateShortcut({target}, {icon})");
            try
            {
                string lnk = Path.ChangeExtension(target, ".lnk");
                CreateLnk(lnk, target, icon, Path.GetDirectoryName(target));
                return OK($"Shortcut created: '{lnk}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ── Hide / unhide file ────────────────────────────────────────────
        static ActionResult SetFileHidden(string file, bool hide)
        {
            if (dryrun) return DryRun($"SetFileHidden({file}, {hide})");
            if (!File.Exists(file)) return Fail($"File not found: {file}");
            try
            {
                if (hide) SetAttr(file, FileAttributes.Hidden);
                else RemoveAttr(file, FileAttributes.Hidden);
                return OK($"File '{file}' {(hide ? "hidden" : "visible")}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ═══════════════════════ QUERY / INFO ══════════════════════════════

        static void QueryExtension(string ext)
        {
            ext = NormExt(ext);
            using var root = Registry.ClassesRoot;
            string progId = GetProgId(ext);
            string icon = root.OpenSubKey($@"{progId}\DefaultIcon")?.GetValue("") as string ?? "(none)";
            string display = root.OpenSubKey(progId)?.GetValue("") as string ?? "(none)";
            string perceived = root.OpenSubKey(ext)?.GetValue("PerceivedType") as string ?? "(none)";
            string ct = root.OpenSubKey(ext)?.GetValue("Content Type") as string ?? "(none)";
            string openCmd = root.OpenSubKey($@"{progId}\shell\open\command")?.GetValue("") as string ?? "(none)";

            // List verbs
            var verbNames = root.OpenSubKey($@"{progId}\shell")?.GetSubKeyNames() ?? Array.Empty<string>();
            string verbs = verbNames.Length == 0 ? "(none)" : string.Join(", ", verbNames);

            output.Add($"""
╔══ Extension: {ext} ══════════════════════════════╗
  ProgId        : {progId}
  Display Name  : {display}
  Icon          : {icon}
  PerceivedType : {perceived}
  Content-Type  : {ct}
  Open Command  : {openCmd}
  Verbs         : {verbs}
╚══════════════════════════════════════════════════╝
""");
        }

        static void QueryFolder(string folder)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            bool exists = File.Exists(ini);
            output.Add($"Folder : {folder}");
            output.Add($"Exists : {Directory.Exists(folder)}");
            output.Add($"desktop.ini : {(exists ? "present" : "absent")}");
            if (exists) output.Add(File.ReadAllText(ini));
            var info = new DirectoryInfo(folder);
            output.Add($"Attributes : {info.Attributes}");
        }

        static void QueryFile(string file)
        {
            var info = new FileInfo(file);
            output.Add($"""
╔══ File: {info.Name} ════════════════════════════╗
  Full Path  : {info.FullName}
  Size       : {info.Length:N0} bytes
  Created    : {info.CreationTime:u}
  Modified   : {info.LastWriteTime:u}
  Attributes : {info.Attributes}
╚══════════════════════════════════════════════════╝
""");
        }

        static void ListAllExtensions()
        {
            using var root = Registry.ClassesRoot;
            var exts = root.GetSubKeyNames()
                .Where(k => k.StartsWith("."))
                .OrderBy(k => k);
            output.Add($"Registered extensions ({exts.Count()}):");
            foreach (var e in exts) output.Add($"  {e}");
        }

        static void ListAllFiles(List<string> roots, List<string> filterExt)
        {
            foreach (var dir in roots)
            {
                if (!Directory.Exists(dir)) { output.Add($"[WARN] Not found: {dir}"); continue; }
                foreach (var file in Directory.EnumerateFiles(dir, "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    if (filterExt.Count == 0 || filterExt.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                        output.Add(file);
                }
            }
        }

        static void ListAllFolders(List<string> roots)
        {
            foreach (var dir in roots)
            {
                if (!Directory.Exists(dir)) { output.Add($"[WARN] Not found: {dir}"); continue; }
                foreach (var d in Directory.EnumerateDirectories(dir, "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    output.Add(d);
            }
        }

        static void ListVerbs(string? ext)
        {
            using var root = Registry.ClassesRoot;
            string progId = ext != null ? GetProgId(NormExt(ext)) : "Directory";
            var verbs = root.OpenSubKey($@"{progId}\shell")?.GetSubKeyNames() ?? Array.Empty<string>();
            output.Add($"Verbs for {(ext ?? "Directory")} / ProgId '{progId}':");
            foreach (var v in verbs)
            {
                string disp = root.OpenSubKey($@"{progId}\shell\{v}")?.GetValue("") as string ?? "";
                string cmd = root.OpenSubKey($@"{progId}\shell\{v}\command")?.GetValue("") as string ?? "";
                output.Add($"  [{v}] '{disp}' → {cmd}");
            }
        }

        // ═══════════════════════ BULK OPS ══════════════════════════════════

        static void BulkAction(List<string> roots, List<string> extFilter, List<string> folderList, List<string> fileList, bool rec)
        {
            output.Add($"Bulk mode | roots={roots.Count} | ext filter={extFilter.Count} | recurse={rec} | dryrun={dryrun}");

            // Process files matching extension filter in root paths
            foreach (var dir in roots)
            {
                if (!Directory.Exists(dir)) { output.Add($"[WARN] {dir} not found"); continue; }
                foreach (var f in Directory.EnumerateFiles(dir, "*",
                    rec ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    if (extFilter.Count > 0 && !extFilter.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Apply queued operations
                    if (extensionIcon != null)
                    {
                        string e = Path.GetExtension(f).ToLower();
                        if (!string.IsNullOrEmpty(e))
                            Log(SetExtensionIcon(e, extensionIcon));
                    }
                    if (verbose) output.Add($"  Processed: {f}");
                }
            }

            // Process explicit folder list
            foreach (var folder in folderList)
            {
                if (folderIcon != null) Log(SetFolderIcon(folder, folderIcon));
                if (verbose) output.Add($"  Folder processed: {folder}");
            }

            // Process explicit file list
            foreach (var file in fileList)
            {
                if (fileIcon != null) Log(SetFileIcon(file, fileIcon));
                if (verbose) output.Add($"  File processed: {file}");
            }
        }

        // ═══════════════════════ BACKUP / RESTORE ══════════════════════════

        static ActionResult BackupAssociations(string outFile)
        {
            if (dryrun) return DryRun($"BackupAssociations({outFile})");
            try
            {
                var data = new BackupData();

                // Snapshot all registered extensions
                using var root = Registry.ClassesRoot;
                foreach (var extName in root.GetSubKeyNames().Where(k => k.StartsWith(".")))
                {
                    string progId = GetProgId(extName);
                    var info = new ExtensionInfo
                    {
                        Extension = extName,
                        ProgId = progId,
                        Icon = root.OpenSubKey($@"{progId}\DefaultIcon")?.GetValue("") as string ?? "",
                        DisplayName = root.OpenSubKey(progId)?.GetValue("") as string ?? "",
                        ContentType = root.OpenSubKey(extName)?.GetValue("Content Type") as string ?? "",
                        PerceivedType = root.OpenSubKey(extName)?.GetValue("PerceivedType") as string ?? "",
                        OpenWith = root.OpenSubKey($@"{progId}\shell\open\command")?.GetValue("") as string ?? "",
                    };

                    // Verbs
                    var shellKey = root.OpenSubKey($@"{progId}\shell");
                    if (shellKey != null)
                        foreach (var vn in shellKey.GetSubKeyNames())
                            info.Verbs.Add(new VerbInfo
                            {
                                Name = vn,
                                DisplayName = shellKey.OpenSubKey(vn)?.GetValue("") as string ?? "",
                                Command = shellKey.OpenSubKey($@"{vn}\command")?.GetValue("") as string ?? ""
                            });

                    data.Extensions.Add(info);
                }

                string json = JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(outFile, json);
                return OK($"Backup saved: '{outFile}' ({data.Extensions.Count} extensions)");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        static ActionResult RestoreAssociations(string inFile)
        {
            if (dryrun) return DryRun($"RestoreAssociations({inFile})");
            if (!File.Exists(inFile)) return Fail($"Backup file not found: {inFile}");
            try
            {
                var data = JsonConvert.DeserializeObject<BackupData>(File.ReadAllText(inFile))!;
                using var root = Registry.ClassesRoot;
                int restored = 0;

                foreach (var ext in data.Extensions)
                {
                    if (!string.IsNullOrEmpty(ext.Icon))
                    { using var k = root.CreateSubKey($@"{ext.ProgId}\DefaultIcon"); k.SetValue("", ext.Icon); }

                    if (!string.IsNullOrEmpty(ext.DisplayName))
                    { using var k = root.CreateSubKey(ext.ProgId); k.SetValue("", ext.DisplayName); }

                    if (!string.IsNullOrEmpty(ext.OpenWith))
                    { using var k = root.CreateSubKey($@"{ext.ProgId}\shell\open\command"); k.SetValue("", ext.OpenWith); }

                    foreach (var verb in ext.Verbs)
                    {
                        using var vk = root.CreateSubKey($@"{ext.ProgId}\shell\{verb.Name}");
                        vk.SetValue("", verb.DisplayName);
                        using var ck = root.CreateSubKey($@"{ext.ProgId}\shell\{verb.Name}\command");
                        ck.SetValue("", verb.Command);
                    }
                    restored++;
                }

                RefreshExplorer();
                return OK($"Restored {restored} extensions from '{inFile}'");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ═══════════════════════ EXPORT / IMPORT JSON ═══════════════════════

        static ActionResult ExportData(string outFile)
        {
            if (dryrun) return DryRun($"ExportData({outFile})");
            return BackupAssociations(outFile); // same format
        }

        static ActionResult ImportData(string inFile)
        {
            if (dryrun) return DryRun($"ImportData({inFile})");
            return RestoreAssociations(inFile);
        }

        // ═══════════════════════ REG EXPORT / IMPORT ════════════════════════

        static ActionResult RegExport(string outFile)
        {
            if (dryrun) return DryRun($"RegExport(HKCR → {outFile})");
            try
            {
                var psi = new ProcessStartInfo("reg", $"export HKCR \"{outFile}\" /y")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                return p.ExitCode == 0
                    ? OK($"Registry HKCR exported to '{outFile}'")
                    : Fail($"reg export failed: {p.StandardError.ReadToEnd()}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        static ActionResult RegImport(string inFile)
        {
            if (dryrun) return DryRun($"RegImport({inFile})");
            if (!File.Exists(inFile)) return Fail($"File not found: {inFile}");
            try
            {
                var psi = new ProcessStartInfo("reg", $"import \"{inFile}\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                RefreshExplorer();
                return p.ExitCode == 0
                    ? OK($"Registry file '{inFile}' imported")
                    : Fail($"reg import failed: {p.StandardError.ReadToEnd()}");
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ═══════════════════════ UNDO ════════════════════════════════════════

        static void PushUndo(string desc, Action action)
            => undoStack.Push(new UndoAction { Description = desc, Undo = action });

        static void PerformUndo()
        {
            if (undoStack.Count == 0) { output.Add("[INFO] Nothing to undo."); return; }
            var u = undoStack.Pop();
            output.Add($"Undoing: {u.Description}");
            if (!dryrun) u.Undo();
        }

        // ═══════════════════════ HELPERS ════════════════════════════════════

        static string NormExt(string ext)
            => ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();

        static string GetProgId(string ext)
        {
            ext = NormExt(ext);
            return Registry.ClassesRoot.OpenSubKey(ext)?.GetValue("") as string
                ?? ext.TrimStart('.') + "_auto_file";
        }

        static string GetOrCreateProgId(string ext)
        {
            ext = NormExt(ext);
            using var extKey = Registry.ClassesRoot.CreateSubKey(ext);
            string? progId = extKey.GetValue("") as string;
            if (string.IsNullOrEmpty(progId))
            {
                progId = ext.TrimStart('.') + "_auto_file";
                extKey.SetValue("", progId);
            }
            Registry.ClassesRoot.CreateSubKey(progId).Close();
            return progId;
        }

        // Desktop.ini parser — returns dict of section → dict of key → value
        static Dictionary<string, Dictionary<string, string>> ParseDesktopIni(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["[.ShellClassInfo]"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            if (!File.Exists(path)) return result;

            string section = "[.ShellClassInfo]";
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line;
                    if (!result.ContainsKey(section))
                        result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else if (line.Contains('='))
                {
                    int eq = line.IndexOf('=');
                    result[section][line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
            return result;
        }

        static void WriteDesktopIni(string folder, string iniPath,
            Dictionary<string, Dictionary<string, string>> content)
        {
            var sb = new StringBuilder();
            foreach (var sec in content)
            {
                sb.AppendLine(sec.Key);
                foreach (var kv in sec.Value) sb.AppendLine($"{kv.Key}={kv.Value}");
                sb.AppendLine();
            }
            if (File.Exists(iniPath)) File.SetAttributes(iniPath, FileAttributes.Normal);
            File.WriteAllText(iniPath, sb.ToString(), Encoding.Unicode);
            SetAttr(iniPath, FileAttributes.Hidden | FileAttributes.System);
            SetAttr(folder, FileAttributes.System);
        }

        static void CreateLnk(string lnkPath, string target, string? icon, string? workDir)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            if (icon != null) sc.IconLocation = icon.Contains(',') ? icon : icon + ",0";
            if (workDir != null) sc.WorkingDirectory = workDir;
            sc.Save();
        }

        static void SetAttr(string path, FileAttributes attrs)
        {
            bool isDir = Directory.Exists(path);
            FileAttributes cur = isDir ? new DirectoryInfo(path).Attributes : new FileInfo(path).Attributes;
            if (isDir) new DirectoryInfo(path).Attributes = cur | attrs;
            else new FileInfo(path).Attributes = cur | attrs;
        }

        static void RemoveAttr(string path, FileAttributes attrs)
        {
            bool isDir = Directory.Exists(path);
            FileAttributes cur = isDir ? new DirectoryInfo(path).Attributes : new FileInfo(path).Attributes;
            if (isDir) new DirectoryInfo(path).Attributes = cur & ~attrs;
            else new FileInfo(path).Attributes = cur & ~attrs;
        }

        static void RefreshExplorer()
        {
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
        }

        static bool IsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool NeedsAdmin()
            => flags.Any(f => f is "-exticon" or "-deleteexticon" or "-displayname" or "-description"
                or "-perceivedtype" or "-contenttype" or "-openwith" or "-contextverb" or "-deleteverb"
                or "-alwaysshowext" or "-nevershowext" or "-thumbnailhandler" or "-newmenutemplate"
                or "-pruneopenwith" or "-backup" or "-restore" or "-import" or "-export"
                or "-regexport" or "-regimport");

        // ── Result helpers ─────────────────────────────────────────────────
        static ActionResult OK(string msg) => new() { Success = true, Details = msg };
        static ActionResult Fail(string msg) => new() { Success = false, Details = msg };
        static ActionResult Fail(Exception ex) => new() { Success = false, Details = ex.Message };
        static ActionResult DryRun(string msg) => new() { Success = true, Details = $"[DRY-RUN] Would: {msg}" };

        static void Log(ActionResult r)
        {
            output.Add(r.ToString());
            if (!r.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(r.ToString());
                Console.ResetColor();
            }
        }

        static void Warn(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] {msg}");
            Console.ResetColor();
        }

        // ═══════════════════════ BANNER / HELP ══════════════════════════════

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("""
╔══════════════════════════════════════════════════════════════╗
║   SuperFileAssoc  — Advanced Windows Association Utility     ║
║   (c) 2024-2026 Muhammad Hussnain / MiSeSys / Contributors   ║
╚══════════════════════════════════════════════════════════════╝
""");
            Console.ResetColor();
        }

        static void ShowAbout()
        {
            Console.WriteLine("""
SuperFileAssoc  v3.0 — Windows File & Folder Association Power Tool
(c) 2024-2026 Muhammad Hussnain, MiSeSys, Contributors
MIT License.

Features: 40+ operations including per-extension icons, per-folder
desktop.ini, per-file shortcut hacks, context verbs, open-with,
perceived types, content types, thumbnail handlers, new-menu
templates, SendTo entries, bulk operations, backup/restore (JSON),
registry export/import, dry-run, undo, and more.
""");
        }

        static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("""
════════════════════════════════════════════════════════════════════════════
  SuperFileAssoc — Usage Reference
  Run as Administrator for registry and system-level operations!
════════════════════════════════════════════════════════════════════════════

── PER-EXTENSION (GLOBAL REGISTRY) ────────────────────────────────────────
  -inExtension <.ext>              Target extension (used by following ops)
  -exticon <.ext> <icon>           Set icon for extension
                                   icon = "C:\path\file.ico,0" or "dll,idx"
  -deleteexticon                   Remove icon for current extension
  -displayname <text>              Set display name (e.g. "My Text File")
  -description <text>              Set FriendlyTypeName
  -perceivedtype <type>            text | image | audio | video | document
  -contenttype <mime>              e.g. text/plain
  -openwith <.ext> <program.exe>   Set default open-with program
  -alwaysshowext                   Always show extension in Explorer
  -nevershowext                    Never show extension in Explorer
  -contextverb <name> <cmd> <disp> Add verb to extension's context menu
  -deleteverb <name>               Remove verb
  -thumbnailhandler <clsid>        Register thumbnail provider CLSID
  -newmenutemplate <.ext> <file>   Add to New-menu with optional template file
  -addtosendto <name> <program>    Add program shortcut to SendTo menu
  -pruneopenwith                   Remove stale Open-With entries

── PER-FOLDER ──────────────────────────────────────────────────────────────
  -foldericon <folder> <icon>      Set custom icon via desktop.ini
  -resetfoldericon <folder>        Remove folder icon (delete desktop.ini)
  -folderinfotip <folder> <tip>    Set tooltip shown on hover
  -folderlocalizedname <folder> <name>  Set display name in Explorer
  -desktopini <folder> <template>  Apply raw desktop.ini content or file
  -resetdesktopini <folder>        Delete desktop.ini
  -folderprotect <folder>          Set ReadOnly attribute on folder
  -folderunprotect <folder>        Remove ReadOnly attribute
  -contextverb <name> <cmd> <disp> (without -inExtension: applies to folders)

── PER-FILE (SHORTCUT HACK) ────────────────────────────────────────────────
  -fileicon <file> <icon>          Create .lnk with icon; hide original
  -shortcuticon <file.lnk> <icon>  Update existing shortcut's icon
  -createshortcut <file> <icon>    Create .lnk beside file
  -hidefile <file>                 Set Hidden attribute
  -unhidefile <file>               Remove Hidden attribute

── BULK / HYBRID ───────────────────────────────────────────────────────────
  -bulk <path1,path2,...>          Bulk mode — target paths
  -inextensions <.x,.y>           Filter by extensions (comma/semicolon)
  -infolders <f1,f2>              Target folders list
  -infiles <f1,f2>                Target files list
  -recurse / -r                   Recurse into subdirectories

── BACKUP / RESTORE / IMPORT / EXPORT ─────────────────────────────────────
  -backup <file.json>             Snapshot all HKCR associations to JSON
  -restore <file.json>            Restore from JSON snapshot
  -export <file.json>             Alias for -backup
  -import <file.json>             Alias for -restore
  -regexport <file.reg>           reg export HKCR to .reg file
  -regimport <file.reg>           reg import .reg file

── QUERY / INFO ────────────────────────────────────────────────────────────
  -queryext <.ext>                Show all info for extension
  -queryfolder <folder>           Show folder attributes & desktop.ini
  -queryfile <file>               Show file info
  -listext                        List all registered extensions
  -listfiles <path>               List files (use -inextensions to filter)
  -listfolders <path>             List subfolders
  -listverbs [.ext]               List verbs for extension or Directory

── MISC ─────────────────────────────────────────────────────────────────────
  -dryrun                         Show what would be done; no changes
  -verbose                        Extra output
  -quiet                          Suppress "Done." message
  -refresh                        Force Explorer refresh (SHChangeNotify)
  -undo                           Undo last operation (in-session only)
  -help / -h / /?                 This help
  -about                          Version & credits

── EXAMPLES ─────────────────────────────────────────────────────────────────
  SuperFileAssoc -exticon .log "C:\Icons\log.ico,0"
  SuperFileAssoc -foldericon "D:\Projects" "C:\Icons\proj.ico,0"
  SuperFileAssoc -openwith .md "C:\Program Files\Notepad++\notepad++.exe"
  SuperFileAssoc -contextverb OpenVSCode "code \"%1\"" "Open in VS Code" -inExtension .cs
  SuperFileAssoc -contextverb OpenTerminal "wt -d \"%1\"" "Open Terminal Here"
  SuperFileAssoc -perceivedtype .log text -contenttype .log text/plain
  SuperFileAssoc -bulk "C:\Work" -inextensions .cs,.vb -recurse -dryrun
  SuperFileAssoc -backup "C:\Backup\assoc_$(date).json"
  SuperFileAssoc -queryext .pdf
  SuperFileAssoc -listverbs .txt
  SuperFileAssoc -alwaysshowext -inExtension .txt
  SuperFileAssoc -addtosendto "Notepad++" "C:\Program Files\Notepad++\notepad++.exe"
  SuperFileAssoc -regexport "C:\Backup\HKCR.reg"

════════════════════════════════════════════════════════════════════════════
""");
            Console.ResetColor();
        }
    }
}