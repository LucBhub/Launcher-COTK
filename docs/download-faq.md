# COTK Launcher — Windows warning / Avertissement Windows

## English

**Windows says "Windows protected your PC" when I open the launcher. Is it safe?**

The COTK launcher is a community project and is not signed with a paid publisher certificate, so
Windows SmartScreen shows a warning for any file it has not seen many times. This is the same as
the previous COTK launcher.

1. Click **More info**.
2. Click **Run anyway**.

**Check that your download is genuine (optional, 10 seconds)**

Open PowerShell in your download folder and run:

```powershell
Get-FileHash .\COTK-Launcher-Next-1.0.6-win-x64.zip -Algorithm SHA256
```

The value must be identical to the one published next to the download:
`d5a68379677b724d9d6edf3e66adf0c4dc80baebfa2f042bd1980c817cac4c89`. If it differs, **do not run it** and tell us on Discord.

Game updates downloaded by the launcher are separately verified with a digital signature that
only COTK can produce, so a tampered update is refused automatically.

## Français

**Windows affiche « Windows a protégé votre ordinateur » à l'ouverture. Est-ce sûr ?**

Le launcher COTK est un projet communautaire et n'est pas signé avec un certificat d'éditeur payant ;
SmartScreen avertit donc pour tout fichier qu'il n'a pas vu un grand nombre de fois. C'est le même
comportement que l'ancien launcher COTK.

1. Cliquez sur **Informations complémentaires**.
2. Cliquez sur **Exécuter quand même**.

**Vérifier que votre téléchargement est authentique (facultatif, 10 secondes)**

Ouvrez PowerShell dans votre dossier de téléchargement :

```powershell
Get-FileHash .\COTK-Launcher-Next-1.0.6-win-x64.zip -Algorithm SHA256
```

La valeur doit être identique à celle publiée à côté du téléchargement :
`d5a68379677b724d9d6edf3e66adf0c4dc80baebfa2f042bd1980c817cac4c89`. Sinon, **ne l'exécutez pas** et prévenez-nous sur Discord.

Les mises à jour du jeu téléchargées par le launcher sont vérifiées séparément par une signature
numérique que seul COTK peut produire : une mise à jour falsifiée est refusée automatiquement.

## 简体中文

**打开启动器时 Windows 显示“Windows 已保护你的电脑”，安全吗？**

COTK 启动器是社区项目，没有使用付费的发布者证书签名，因此 Windows SmartScreen 会对它不常见的文件显示警告。
旧版 COTK 启动器也是如此。

1. 点击 **更多信息**。
2. 点击 **仍要运行**。

**核对下载文件是否真实（可选，约 10 秒）**

在下载文件夹中打开 PowerShell：

```powershell
Get-FileHash .\COTK-Launcher-Next-1.0.6-win-x64.zip -Algorithm SHA256
```

结果必须与下载页面旁公布的值一致：`d5a68379677b724d9d6edf3e66adf0c4dc80baebfa2f042bd1980c817cac4c89`。如果不一致，**请不要运行**，并在 Discord 告知我们。

启动器下载的游戏更新还会由只有 COTK 才能生成的数字签名单独校验，被篡改的更新会被自动拒绝。
