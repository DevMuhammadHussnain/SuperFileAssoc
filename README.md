# SuperFileAssoc

SuperFileAssoc is an advanced Windows command-line utility for managing file and folder associations from one tool.  
It focuses on Registry- and Explorer-level customization, including:

- File extension association management (`.ext` icons, display names, descriptions, MIME/content type, perceived type, Open With)
- Context-menu verb management for extensions and folders
- Folder customization through `desktop.ini` (icon, localized name, infotip, protection attributes)
- File presentation tweaks (shortcut/icon workflows, hide/unhide, shortcut creation)
- Query and listing commands (inspect extensions, files, folders, and verbs)
- Bulk actions across multiple paths with extension filters and recursive mode
- Safety and recovery features such as dry-run, in-session undo, JSON backup/restore, and `.reg` export/import

The project is built as a single .NET Windows CLI app (`src/SuperFileAssoc/Program.cs`) that parses command flags and executes targeted operations for extension, folder, file, hybrid, and bulk scenarios.

## Official Links

- Official website: https://mise-sys.vercel.app/
- Help: https://mise-sys.vercel.app/comments/help
- Product card: https://mise-sys.vercel.app/products/6a1c20e32f045d50160add26
- Review: https://mise-sys.vercel.app/products/6a1c20e32f045d50160add26
- Need help? misesysofficial+help@gmail.com
