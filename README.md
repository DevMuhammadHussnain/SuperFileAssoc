# ✨ SuperFileAssoc — Premium File Association Manager

<div align="center">

![Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows&logoColor=white)
![PowerShell](https://img.shields.io/badge/PowerShell-5.1%2B-5391FE?style=for-the-badge&logo=powershell&logoColor=white)
![Status](https://img.shields.io/badge/Status-Active-2EA043?style=for-the-badge)
![Safety](https://img.shields.io/badge/Safety-Use%20with%20Caution-F85149?style=for-the-badge)

**A powerful Windows file-association utility for advanced users and administrators.**

</div>

---

## 🧠 Overview

**SuperFileAssoc** is a Windows-focused utility designed to inspect, create, update, and repair file associations.
It helps you map extensions (like `.txt`, `.pdf`, `.ps1`) to applications and ProgIDs in a consistent and repeatable way.

This project is intended for:

- 🛠️ Power users
- 🏢 IT administrators
- 🧪 Test/lab environments
- ⚙️ Automation and deployment workflows

If you're dealing with broken default app mappings, invalid ProgIDs, or deployment scripts that need deterministic association behavior, this tool aims to help.

---

## 🌟 Core Features

- 📎 **Set file associations** for one or more extensions.
- 🔁 **Update existing mappings** without manual registry navigation.
- 🔍 **Inspect current associations** to verify effective state.
- 🧩 **Work with ProgIDs** for predictable association management.
- ⚡ **Script-friendly workflow** for automation.
- 🧰 **Troubleshooting support** for malformed or conflicting entries.
- 🛡️ **Admin-aware actions** for operations requiring elevated rights.

---

## 🧭 Typical Use Cases

- ✅ Standardize default apps across multiple systems.
- ✅ Recover from app uninstall/reinstall association corruption.
- ✅ Fix “Open with” anomalies.
- ✅ Prepare clean association states in VM or CI test environments.
- ✅ Apply custom app mappings during device setup.

---

## ⚠️ Important Safety Notice

> [!WARNING]
> This tool can modify **system and user-level association behavior**.
> Incorrect usage may break file opening behavior, context menus, or user defaults.

Always treat association changes as **high-impact configuration operations**.

### 🔐 Recommended Safety Checklist

Before making changes:

- 💾 Create a **System Restore Point**.
- 🗂️ Export relevant registry keys as backup.
- 🧪 Test on a non-production machine first.
- 👤 Confirm whether action targets **Current User** or **All Users/System** scope.
- 🔁 Keep rollback commands/scripts ready.

---

## 🧱 Possible Issues You May Encounter

Even with correct usage, Windows association logic can be complex.

- ❗ **Hash-protected user choice settings** may prevent direct default app overrides.
- ❗ **Policy-enforced defaults** (Group Policy / MDM) can revert your changes.
- ❗ **Application self-repair** can reclaim associations after updates.
- ❗ **Mixed-scope conflicts** (HKCU vs HKLM) may produce inconsistent results.
- ❗ **Missing or invalid ProgID** can result in “Unknown application” behavior.
- ❗ **Shell cache delays** may make changes appear not applied immediately.

---

## 🚧 Known Limitations

- 🪟 Windows-specific behavior; not intended for Linux/macOS.
- 🔒 Some defaults cannot be forced due to Windows protection mechanisms.
- 🏢 Enterprise policy can override local changes.
- 🧩 Behavior may differ slightly between Windows builds.
- 🧪 Third-party security tools may block registry edits.
- 🧍 Per-user contexts may require running actions under each target profile.

---

## ☠️ Potentially Dangerous Actions

The following operations require extra caution:

- 🔥 Rebinding common extensions (`.txt`, `.pdf`, `.html`, `.json`, etc.) globally.
- 🔥 Overwriting existing ProgIDs without compatibility checks.
- 🔥 Deleting association keys without dependency review.
- 🔥 Bulk-association scripts executed across many machines without staging.
- 🔥 Running elevated operations from unverified scripts.

> [!CAUTION]
> A small mistake can affect many file types and users quickly.
> Use staged rollout and verification.

---

## ✅ Best Practices

- Start with read-only inspection before write operations.
- Apply the smallest possible change set.
- Version-control your scripts/configuration inputs.
- Document original values before modifying anything.
- Use canary devices before broad deployment.
- Validate behavior by opening representative files post-change.

---

## 🧪 Validation & Verification

After updates, verify:

- 📂 File opens in expected application.
- 🖱️ Context menu entries remain intact.
- 🔗 “Open with” shows expected defaults.
- 👥 Multiple user profiles behave as expected.
- 🔁 Settings persist after reboot and app updates.

---

## 🛠️ Troubleshooting Guide

If associations don’t stick:

1. Check for Group Policy / MDM enforcement.
2. Confirm elevation where required.
3. Verify ProgID exists and points to valid open command.
4. Restart Explorer or sign out/in.
5. Re-test in clean user profile.
6. Review app-specific “Set as default” reclaim behavior.

---

## 🤝 Contribution Guidelines

Contributions are welcome.

When submitting improvements:

- Keep safety warnings explicit.
- Include test notes for affected extensions.
- Describe user vs system scope clearly.
- Avoid destructive default behavior.

---

## 📜 Disclaimer

This tool is provided **as-is**, without warranties.

By using SuperFileAssoc, you acknowledge that file-association and registry operations can impact system stability and user workflows. You are responsible for testing, backups, and safe rollout.

---

<div align="center">

### 💎 Built for precision. Used with caution.

</div>
