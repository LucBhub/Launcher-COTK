namespace COTK.Next.Core;

internal static partial class L
{
    // French source | English | Simplified Chinese. Technical identifiers and user content are not keys.
    private const string Catalogue = """
Langue|Language|语言
Pause|Pause|暂停
Reprendre|Resume|继续
En pause|Paused|已暂停
Téléchargement en pause|Download paused|下载已暂停
Serveur de jeu en ligne|Game server online|游戏服务器在线
{0} joueurs|{0} players|{0} 名玩家
Serveur de jeu en ligne · {0} joueurs|Game server online · {0} players|游戏服务器在线 · {0} 名玩家
État du jeu inconnu|Game status unknown|游戏状态未知
Connexion impossible|Connection unavailable|无法连接
Maintenance en cours|Maintenance in progress|正在维护
Le serveur de jeu est en maintenance. Réessayez plus tard.|The game server is under maintenance. Try again later.|游戏服务器正在维护，请稍后重试。
Installation incomplète|Installation incomplete|安装不完整
Un fichier essentiel manque : {0}.|An essential file is missing: {0}.|缺少必要文件：{0}。
Espace disque non vérifiable|Unable to verify disk space|无法验证磁盘空间
Impossible de vérifier l'espace libre sur le disque choisi. L'installation est bloquée.|Free space on the selected disk could not be verified. Installation is blocked.|无法验证所选磁盘的可用空间，已阻止安装。
Choisissez un disque local accessible puis réessayez.|Choose an accessible local disk and retry.|请选择可访问的本地磁盘后重试。
Dossier : {0}|Folder: {0}|文件夹：{0}
Téléchargement : {0}|Download: {0}|下载：{0}
Espace nécessaire : {0} · Disponible : {1}|Required space: {0} · Available: {1}|所需空间：{0} · 可用空间：{1}
Désinstaller le client|Uninstall client|卸载客户端
Désinstaller le client ?|Uninstall the game client?|卸载游戏客户端？
Désinstallation|Uninstalling|卸载
Préparation de la désinstallation…|Preparing to uninstall…|正在准备卸载…
Suppression des fichiers du client…|Removing game client files…|正在删除游戏客户端文件…
Les fichiers enregistrés du jeu seront supprimés. Vos réglages, touches, logs et fichiers personnels inconnus seront conservés. Le launcher et votre compte ne seront pas supprimés.|Registered game files will be removed. Your settings, keybindings, logs and unrecognized personal files will be kept. The launcher and your account will remain.|将删除登记的游戏文件。您的设置、按键绑定、日志和未知的个人文件会保留，不会删除启动器或账号。
{0} fichiers enregistrés · {1}|{0} registered files · {1}|{0} 个登记文件 · {1}
Client désinstallé|Client uninstalled|客户端已卸载
{0} fichiers supprimés · {1} libérés. Vos données conservées restent dans le dossier.|{0} files removed · {1} freed. Saved data remains in the folder.|已删除 {0} 个文件 · 已释放 {1} 空间。保留的数据仍在原文件夹中。
{0}/{1} fichiers|{0}/{1} files|{0}/{1} 个文件
Installation non reconnue|Installation not recognized|无法识别安装
Le dossier ne contient pas un client COTK identifié. Aucun fichier ne sera supprimé.|The folder does not contain a recognized COTK client. No files will be removed.|该文件夹中没有可识别的 COTK 客户端，不会删除任何文件。
Liste de fichiers indisponible|File list unavailable|文件列表不可用
Impossible d'identifier les fichiers du client. Connectez-vous pour récupérer son catalogue puis réessayez.|Could not identify the client files. Connect to download its catalogue, then retry.|无法识别客户端文件。请联网下载文件列表后重试。
Désinstallation interrompue|Uninstall interrupted|卸载已中断
Impossible de supprimer : {0}.|Could not remove: {0}.|无法删除：{0}。
Fermez le programme qui verrouille ce fichier puis relancez la désinstallation. Vos données conservées sont intactes.|Close the program locking this file, then retry the uninstall. Your retained data is intact.|请关闭锁定该文件的程序后重新卸载。保留的数据仍然完好。
Désinstallation refusée|Uninstall blocked|卸载被阻止
Le dossier ou sa liste de fichiers n'est pas sûr. Aucun fichier personnel ne sera supprimé.|The folder or its file list is unsafe. No personal files will be removed.|该文件夹或其文件列表不安全，不会删除个人文件。
Fichier personnel en conflit|Personal file conflict|个人文件冲突
Un fichier conservé existe aussi dans le nouveau client : {0}.|A retained file conflicts with a new client file: {0}.|保留的文件与新客户端文件冲突：{0}。
Désinstallation en cours|Uninstall in progress|正在卸载
Attendez la fin de la désinstallation avant de fermer le launcher.|Wait for the uninstall to finish before closing the launcher.|请等待卸载完成后再关闭启动器。
La désinstallation n'a pas commencé. Aucun fichier n'a été supprimé.|The uninstall has not started. No files have been removed.|卸载尚未开始，没有删除任何文件。
Un launcher COTK est déjà ouvert. Fermez l'ancienne version avant d'ouvrir la refonte.|A COTK launcher is already open. Close the previous launcher before opening this one.|COTK 启动器已经打开，请先关闭旧启动器。
COTK — le launcher ne peut pas démarrer|COTK — launcher could not start|COTK——启动器无法启动
Langue du launcher|Launcher language|启动器语言
Changement immédiat, choix mémorisé sur cet ordinateur.|Changes apply immediately and are saved on this computer.|更改立即生效，并保存在此电脑上。
Accueil|Home|首页
Bibliothèque|Library|游戏库
Matchs|Matches|对局
Classement|Leaderboard|排行榜
Paramètres|Settings|设置
Bon retour,|Welcome back,|欢迎回来，
Compte|Account|账号
Jeu|Game|游戏
Affichage|Display|显示
Support|Support|支持
À propos|About|关于
Déconnecté|Signed out|未登录
ADMIN|ADMIN|管理员
MODÉRATEUR|MODERATOR|版主
Administrateur|Administrator|管理员
Modérateur|Moderator|版主
Survivant|Survivor|幸存者
Se déconnecter|Sign out|退出登录
Session protégée par le Gestionnaire d'identification Windows|Session protected by Windows Credential Manager|会话由 Windows 凭据管理器保护
Les sessions staff ne sont jamais mémorisées sur ce PC.|Staff sessions are never saved on this computer.|工作人员会话不会保存在此电脑上。
Dossier du jeu|Game folder|游戏文件夹
Dossier d'installation|Installation folder|安装文件夹
Changer…|Change…|更改…
Ouvrir|Open|打开
Ouvrir le dossier|Open folder|打开文件夹
Réduire le launcher pendant la partie|Minimize the launcher while playing|游戏时最小化启动器
Le launcher se met dans la barre des tâches quand le jeu démarre et revient à la fermeture.|The launcher minimizes when the game starts and returns when it closes.|游戏启动时，启动器将最小化；游戏关闭后，启动器将恢复。
Réduire les animations|Reduce motion|减少动画
Désactive braises, parallaxe et transitions. Recommandé sur un PC modeste ; suit le réglage de Windows par défaut.|Disables embers, parallax and transitions. Recommended for slower computers; follows Windows settings by default.|关闭火星、视差和过渡动画。建议性能较低的电脑开启；默认遵循 Windows 设置。
Exporter le rapport|Export report|导出报告
Ouvrir les journaux|Open logs|打开日志
Choisir un extrait…|Choose an excerpt…|选择日志片段…
Envoyer des logs au support|Send logs to support|向支持团队发送日志
Envoi volontaire, aperçu expurgé, numéro de suivi et conservation de sept jours.|Optional upload with a redacted preview, reference number and seven-day retention.|自愿发送，提供脱敏预览和报告编号，保存七天。
Rapport de diagnostic|Diagnostic report|诊断报告
Un fichier .zip (versions, journaux du launcher et du client, sans mot de passe ni ticket) à envoyer au support.|A .zip with versions and launcher/game logs, without passwords or tickets, for the support team.|生成包含版本信息和启动器、游戏日志的 .zip 文件，供支持团队使用，不含密码或登录票据。
Journaux détaillés|Verbose logging|详细日志
À activer seulement si le support vous le demande.|Enable only when requested by support.|仅在支持团队要求时开启。
Site et compte|Website and account|网站与账号
Créer un compte, réinitialiser un mot de passe, boutique.|Create an account, reset a password, visit the shop.|创建账号、重置密码或访问商店。
Lire les rapports|Read reports|查看报告
Rapports reçus · staff|Received reports · staff|已收到的报告 · 工作人员
Extraits reçus sur la production, accessibles seulement aux membres du staff.|Reports received on production, accessible only to staff.|正式服务器收到的日志片段，仅工作人员可以访问。
Projet communautaire à but éducatif, non affilié à Daybreak Game Company, Z1 Games ni aucun détenteur des droits sur H1Z1. Aucun asset du jeu n'est distribué avec le launcher.|An educational community project, not affiliated with Daybreak Game Company, Z1 Games or H1Z1 rights holders.|这是一个社区教育项目，与 Daybreak Game Company、Z1 Games 或 H1Z1 的权利持有人无关联。
Paliers de rang|Rank tiers|段位等级
Actualités et mises à jour|News and updates|新闻与更新
Pré-saison 3 · battle royale|Pre-season 3 · battle royale|季前赛 3 · 大逃杀
Le dernier survivant porte la couronne. Atterrissez, pillez, survivez — le classement se joue maintenant.|The last survivor wears the crown. Land, loot, survive — your climb starts now.|最后的幸存者将戴上王冠。降落、搜刮、生存——现在开始攀登排行榜。
SOLO|SOLO|单人
150 JOUEURS|150 PLAYERS|150 名玩家
150 JOUEURS MAX|150 PLAYERS MAX|最多 150 名玩家
CARTE Z2|Z2 MAP|Z2 地图
Lire les notes complètes →|Read full patch notes →|阅读完整更新说明 →
Score classé|Ranked score|排位积分
Rang|Rank|排名
Matchs classés|Ranked matches|排位对局
Victoires · 10 derniers|Wins · last 10|胜场 · 最近 10 局
Éliminations · 10 derniers|Kills · last 10|击杀 · 最近 10 局
Profil détaillé bientôt disponible|Detailed profile coming soon|详细资料即将开放
Le serveur ne fournit pas encore les statistiques personnelles au nouveau launcher. Le classement public reste consultable.|The server does not yet provide personal statistics to this launcher. The public leaderboard remains available.|服务器尚未向此启动器提供个人统计数据，仍可查看公开排行榜。
/ {0} pour {1}|/ {0} to reach {1}|/ 达到 {1} 需要 {0}
Saison {0}|Season {0}|赛季 {0}
Départ|Starting tier|起始段位
Non classé|Unranked|未定级
Bronze|Bronze|青铜
Argent|Silver|白银
Or|Gold|黄金
Platine|Platinum|铂金
Diamant|Diamond|钻石
Royauté|Royalty|王者
Jouez {0} matchs classés pour obtenir votre premier rang.|Play {0} ranked matches to earn your first rank.|完成 {0} 场排位对局后即可获得首个段位。
Encore {0} matchs classés pour obtenir votre rang ({1}/{2}).|Play {0} more ranked matches to earn your rank ({1}/{2}).|再完成 {0} 场排位对局即可获得段位（{1}/{2}）。
Encore {0} match classé pour obtenir votre rang ({1}/{2}).|Play {0} more ranked match to earn your rank ({1}/{2}).|再完成 {0} 场排位对局即可获得段位（{1}/{2}）。
Prochain palier : {0} — encore {1} points.|Next tier: {0} — {1} points to go.|下一段位：{0}——还差 {1} 分。
Palier maximal atteint. Vous régnez sur le classement.|Highest tier reached. You rule the leaderboard.|已达到最高段位，您称霸排行榜。
Rien de neuf pour l'instant|Nothing new just yet|暂时没有新消息
Les annonces et notes de patch apparaîtront ici.|Announcements and patch notes will appear here.|公告和更新说明将在此处显示。
Les meilleurs survivants de la saison en cours.|The best survivors of the current season.|本赛季最优秀的幸存者。
Actualiser|Refresh|刷新
Le classement est vide|The leaderboard is empty|排行榜为空
Dès qu'un match classé sera terminé, les meilleurs scores apparaîtront ici.|Top scores will appear as soon as a ranked match is completed.|首场排位对局结束后，最高分将显示在此处。
JOUEUR|PLAYER|玩家
PALIER|TIER|段位
SCORE|SCORE|积分
VOUS|YOU|你
Jouer|Play|开始游戏
Vérifier les fichiers|Verify files|验证文件
Réparer|Repair|修复
Client du jeu|Game client|游戏客户端
Pré-saison 3 · client PS3 authentique|Pre-season 3 · original PS3 client|季前赛 3 · 原版 PS3 客户端
Version installée|Installed version|已安装版本
Version publiée|Latest version|最新版本
Téléchargement|Download|下载
Réparer le client ?|Repair the game client?|修复游戏客户端？
Tous les fichiers du jeu seront relus et comparés au serveur ; ceux qui sont corrompus ou manquants seront retéléchargés. Vos réglages et touches sont conservés. Cela peut prendre plusieurs minutes.|All game files will be checked against the server. Missing or damaged files will be downloaded again. Your settings and keybindings are preserved. This may take several minutes.|将读取所有游戏文件并与服务器比较，重新下载损坏或缺失的文件。您的设置和按键绑定会保留，此过程可能需要几分钟。
Lancer la réparation|Start repair|开始修复
À jour|Up to date|已是最新版本
Mise à jour disponible|Update available|有可用更新
Non installé|Not installed|未安装
Vérification…|Checking…|正在检查…
Indisponible|Unavailable|不可用
Mettre à jour|Update|更新
Installer le jeu|Install game|安装游戏
Réessayer|Retry|重试
Espace disque|Disk space|磁盘空间
libres sur {0}|free on {0}|可用空间，磁盘 {0}
L'installation demande environ {0} le temps de l'extraction.|Installation needs about {0} while extracting.|解压期间安装大约需要 {0} 空间。
Attention : il faut environ {0} pour installer le jeu.|About {0} is needed to install the game.|安装游戏大约需要 {0} 空间。
Information indisponible|Information unavailable|信息不可用
Connectez-vous pour rejoindre le combat.|Sign in to join the fight.|登录后加入战斗。
Vérification de votre session…|Checking your session…|正在验证会话…
Se connecter avec le navigateur|Sign in with your browser|通过浏览器登录
Recommandé : vous validez sur cotk.fr, votre mot de passe ne passe jamais par le launcher.|Recommended: authorize on cotk.fr. Your password never passes through the launcher.|推荐：在 cotk.fr 上验证，您的密码不会经过启动器。
Identifiant et mot de passe|Username and password|用户名与密码
Pas encore de compte ? Créer un compte sur cotk.fr →|No account yet? Create one on cotk.fr →|还没有账号？在 cotk.fr 上创建 →
Connexion au service COTK…|Connecting to COTK…|正在连接 COTK…
Étape 1 · ouvrez le site|Step 1 · open the website|第 1 步 · 打开网站
Entrez ce code sur cotk.fr pour autoriser le launcher :|Enter this code on cotk.fr to authorize the launcher:|在 cotk.fr 输入此验证码以授权启动器：
Ouvrir le site|Open website|打开网站
Copier le code|Copy code|复制验证码
Copié ✓|Copied ✓|已复制 ✓
Copie impossible|Could not copy|复制失败
En attente de votre validation…|Waiting for your approval…|等待您的确认…
Annuler|Cancel|取消
expire dans {0}:{1}|expires in {0}:{1}|将在 {0}:{1} 后过期
Pseudo ou e-mail|Username or email|用户名或电子邮箱
Mot de passe|Password|密码
Se connecter|Sign in|登录
Saisissez votre identifiant et votre mot de passe.|Enter your username and password.|请输入用户名和密码。
Utiliser le navigateur plutôt|Use your browser instead|改用浏览器登录
ou|or|或
Historique indisponible|History unavailable|历史记录不可用
Le backend ne fournit pas encore le profil détaillé au nouveau launcher. Aucun résultat personnel n'est inventé.|The server does not yet provide detailed profiles to this launcher. No personal results are fabricated.|服务器尚未向此启动器提供详细资料，不会生成虚假的个人成绩。
Retour à l'accueil|Back to home|返回首页
Aucun match classé cette saison|No ranked matches this season|本赛季暂无排位对局
Lancez une partie : vos placements, scores et éliminations apparaîtront ici.|Play a match: your placements, scores and kills will appear here.|开始一场对局，您的排名、积分和击杀数将显示在此处。
Meilleur score|Best score|最高积分
Meilleure place|Best placement|最佳名次
Victoires|Wins|胜场
Éliminations|Kills|击杀
{0} derniers matchs classés|Last {0} ranked matches|最近 {0} 场排位对局
Préparation…|Preparing…|正在准备…
Vérification|Verification|验证
Réparation|Repair|修复
Installation|Installation|安装
Préparation|Preparing|准备
Analyse|Analysis|分析
Extraction|Extraction|解压
Nettoyage|Cleanup|清理
Terminé|Done|完成
Serveur|Server|服务器
Session|Session|会话
Client|Client|客户端
Monde|World|世界
En jeu|In game|游戏中
{0} / {1} · {2}/{3} fichiers|{0} / {1} · {2}/{3} files|{0} / {1} · {2}/{3} 个文件
{0} · reprise automatique si la connexion saute|{0} · automatically resumes if the connection drops|{0} · 连接中断后自动继续下载
Versions indisponibles — vérifiez votre connexion|Versions unavailable — check your connection|版本信息不可用——请检查网络连接
Services disponibles · API {0} ms|Services available · API {0} ms|服务可用 · API {0} 毫秒
Services injoignables pour le moment|Services are currently unreachable|服务暂时无法连接
Vérification du serveur…|Checking the server…|正在检查服务器…
En jeu — bon combat !|In game — good luck!|游戏中——祝您好运！
Démarrage…|Starting…|正在启动…
Tentative {0}/{1}|Attempt {0}/{1}|尝试 {0}/{1}
Mise à jour complète du client|Full client update|完整客户端更新
Installer le client du jeu|Install the game client|安装游戏客户端
Les fichiers ne peuvent pas être mis à jour un par un. Le client complet doit être retéléchargé.|Files cannot be updated individually. The full client must be downloaded again.|无法逐个更新文件，必须重新下载完整客户端。
Le client du jeu doit être téléchargé depuis les serveurs COTK. Vous pouvez suivre et annuler le téléchargement à tout moment.|The game client must be downloaded from COTK servers. You can track progress or cancel at any time.|需要从 COTK 服务器下载游戏客户端，您可以随时查看进度或取消下载。
Télécharger|Download|下载
Taille : {0} · Dossier : {1}|Size: {0} · Folder: {1}|大小：{0} · 文件夹：{1}
Où installer le client du jeu ? Un dossier « client » y sera créé.|Where should the game be installed? A "client" folder will be created there.|游戏应安装在哪里？将在所选位置创建“client”文件夹。
Impossible de lire la session enregistrée sur ce PC. Reconnectez-vous.|Could not read the saved session on this computer. Sign in again.|无法读取此电脑上保存的会话，请重新登录。
Votre session a expiré. Reconnectez-vous.|Your session has expired. Sign in again.|您的会话已过期，请重新登录。
Mode aperçu : aucun appel d'authentification.|Preview mode: no authentication requests are sent.|预览模式：不会发送身份验证请求。
Session non mémorisée|Session not saved|会话未保存
Vous devrez vous reconnecter au prochain lancement.|You will need to sign in again next time.|下次启动时您需要重新登录。
Session fermée sur cet ordinateur.|Signed out on this computer.|已在此电脑上退出登录。
Versions indisponibles|Versions unavailable|版本信息不可用
Opération annulée|Operation cancelled|操作已取消
Le téléchargement est conservé : il reprendra où il s'est arrêté.|Your download is saved and will resume where it stopped.|已保留下载进度，将从中断处继续。
Client mis à jour|Client updated|客户端已更新
Client installé|Client installed|客户端已安装
Réparation terminée|Repair complete|修复完成
Fichiers vérifiés|Files verified|文件已验证
{0} fichiers contrôlés — tout est intact.|{0} files checked — everything is intact.|已检查 {0} 个文件——全部完好。
{0} fichier(s) corrigé(s), {1} retéléchargés.|{0} file(s) repaired, {1} downloaded again.|已修复 {0} 个文件，重新下载了 {1}。
Partie terminée|Game closed|游戏已关闭
Content de vous revoir. Prêt pour la suivante ?|Welcome back. Ready for another round?|欢迎回来，准备好下一局了吗？
Version {0} installée (archive complète).|Version {0} installed (full archive).|已安装版本 {0}（完整压缩包）。
Version {0} prête à jouer.|Version {0} ready to play.|版本 {0} 已准备就绪。
Le client était déjà à jour.|The client was already up to date.|客户端已经是最新版本。
{0} fichiers vérifiés ({1}).|{0} files verified ({1}).|已验证 {0} 个文件（{1}）。
{0} : {1} fichier(s), {2}.|{0}: {1} file(s), {2}.|{0}：{1} 个文件，{2}。
Consentement requis|Consent required|需要同意
Confirmez l'envoi de l'extrait affiché.|Confirm that you want to send the displayed excerpt.|请确认发送显示的日志片段。
APERÇU — AUCUN ENVOI|PREVIEW — NOTHING SENT|预览——未发送任何内容
Opération en cours|Operation in progress|正在执行操作
Attendez la fin de l'opération avant de changer de dossier.|Wait for the operation to finish before changing folders.|请等待操作完成后再更改文件夹。
Choisissez le dossier qui contient (ou contiendra) le client du jeu.|Select the folder that contains (or will contain) the game client.|选择包含（或将包含）游戏客户端的文件夹。
Dossier enregistré|Folder saved|文件夹已保存
Téléchargement du client…|Downloading game client…|正在下载游戏客户端…
Vérification de l'intégrité…|Checking integrity…|正在验证完整性…
Extraction des fichiers…|Extracting files…|正在解压文件…
Analyse des fichiers…|Scanning files…|正在分析文件…
Mode aperçu : aucune donnée réelle n'a été modifiée.|Preview mode: no real data was changed.|预览模式：未更改任何真实数据。
Préparation de votre session…|Preparing your session…|正在准备您的会话…
Démarrage du client…|Starting the client…|正在启动客户端…
Client prêt — choisissez « Enter Game ».|Client ready — select "Enter Game".|客户端已准备就绪——请选择“Enter Game”。
Chargement du monde…|Loading the world…|正在加载世界…
En jeu. Bon combat !|In game. Good luck!|游戏中，祝您好运！
Mode aperçu : le jeu n'a pas été lancé.|Preview mode: the game was not launched.|预览模式：未启动游戏。
Action annulée.|Action cancelled.|操作已取消。
Pas de connexion|No connection|无网络连接
Impossible de joindre les serveurs COTK.|Could not reach the COTK servers.|无法连接 COTK 服务器。
Vérifiez votre connexion Internet (ou un pare-feu / VPN), puis réessayez.|Check your internet connection, firewall or VPN, then retry.|请检查网络连接、防火墙或 VPN，然后重试。
Le serveur met trop de temps à répondre|The server is taking too long to respond|服务器响应超时
La requête a expiré.|The request timed out.|请求已超时。
Votre connexion est peut-être instable. Réessayez dans un instant.|Your connection may be unstable. Try again shortly.|您的网络可能不稳定，请稍后重试。
Connexion sécurisée impossible|Secure connection failed|安全连接失败
Le certificat du serveur n'a pas pu être vérifié.|The server certificate could not be verified.|无法验证服务器证书。
Vérifiez la date et l'heure de votre PC (un décalage invalide les certificats), puis réessayez.|Check your computer's date and time, then retry. Incorrect time can invalidate certificates.|请检查电脑的日期和时间后重试，错误的时间会导致证书验证失败。
Trop de tentatives|Too many attempts|尝试次数过多
Réessayez dans {0} secondes.|Try again in {0} seconds.|请在 {0} 秒后重试。
Patientez une minute avant de réessayer.|Wait a minute before trying again.|请等待一分钟后再试。
Les serveurs COTK rencontrent un problème|COTK servers are having trouble|COTK 服务器遇到问题
Le service a répondu par une erreur (HTTP {0}).|The service returned an error (HTTP {0}).|服务返回错误（HTTP {0}）。
Ce n'est pas de votre côté. Réessayez dans quelques minutes.|This is a server-side issue. Try again in a few minutes.|这是服务器端的问题，请几分钟后重试。
Réponse inattendue du serveur|Unexpected server response|服务器响应异常
Le serveur a renvoyé une réponse que le launcher ne comprend pas.|The server returned a response the launcher could not understand.|服务器返回了启动器无法识别的响应。
Mettez à jour le launcher ou réessayez plus tard.|Update the launcher or try again later.|请更新启动器或稍后重试。
Identifiants incorrects|Incorrect credentials|登录信息错误
Le pseudo (ou l'e-mail) ou le mot de passe est faux.|The username, email or password is incorrect.|用户名、电子邮箱或密码不正确。
Mot de passe oublié ? Réinitialisez-le sur cotk.fr.|Forgot your password? Reset it on cotk.fr.|忘记密码？请在 cotk.fr 重置。
Compte suspendu|Account suspended|账号已被暂停
Ce compte n'a pas le droit de jouer actuellement.|This account is currently not allowed to play.|此账号目前无法进行游戏。
Contactez l'équipe COTK sur Discord avec ce code.|Contact the COTK team on Discord with this code.|请通过 Discord 联系 COTK 团队并提供此代码。
Session expirée|Session expired|会话已过期
Votre session a expiré ou a été révoquée.|Your session has expired or was revoked.|您的会话已过期或被撤销。
Reconnectez-vous.|Sign in again.|请重新登录。
Code expiré|Code expired|验证码已过期
Le code de connexion n'est plus valide.|The sign-in code is no longer valid.|登录验证码已失效。
Relancez la connexion pour obtenir un nouveau code.|Start signing in again to get a new code.|请重新开始登录以获取新的验证码。
Compte non vérifié|Account not verified|账号尚未验证
Votre adresse e-mail n'est pas encore confirmée.|Your email address has not been verified yet.|您的电子邮箱尚未确认。
Cliquez sur le lien reçu par e-mail, puis reconnectez-vous.|Follow the link in your email, then sign in again.|点击邮件中的链接，然后重新登录。
Espace disque insuffisant|Not enough disk space|磁盘空间不足
Il faut {0} de libre, il n'en reste que {1}.|{0} of free space is required, but only {1} is available.|需要 {0} 可用空间，但目前仅有 {1}。
Le disque est plein.|The disk is full.|磁盘已满。
Libérez de l'espace ou choisissez un autre disque dans Paramètres.|Free up space or select another drive in Settings.|请释放空间或在设置中选择其他磁盘。
Accès refusé à un fichier|File access denied|文件访问被拒绝
Windows ou un antivirus bloque l'accès à : {0}|Windows or your antivirus is blocking access to: {0}|Windows 或杀毒软件阻止访问：{0}
Fermez le jeu, ajoutez le dossier du jeu aux exclusions de votre antivirus, puis réessayez.|Close the game, add its folder to your antivirus exclusions, then retry.|请关闭游戏，将游戏文件夹加入杀毒软件排除列表，然后重试。
Chemin trop long|Path too long|路径过长
Le chemin est trop long pour Windows : {0}|The path is too long for Windows: {0}|此路径对 Windows 来说过长：{0}
Choisissez un dossier plus court (par exemple D:\Games\COTK).|Choose a shorter folder path, such as D:\Games\COTK.|请选择较短的文件夹路径，例如 D:\Games\COTK。
Dossier non adapté|Unsuitable folder|文件夹不适用
Le dossier choisi existe déjà et n'est pas vide : {0}|The selected folder already exists and is not empty: {0}|所选文件夹已存在且非空：{0}
Choisissez un dossier dédié ou videz-le.|Choose a dedicated folder or empty it first.|请选择专用文件夹，或先清空该文件夹。
Informations de version indisponibles|Version information unavailable|版本信息不可用
Le launcher n'a pas pu lire la liste des versions publiées.|The launcher could not read the published version list.|启动器无法读取已发布的版本列表。
Réessayez dans un instant.|Try again shortly.|请稍后重试。
Fichier corrompu|Corrupted file|文件损坏
La vérification d'intégrité a échoué pour : {0}.|Integrity verification failed for: {0}.|完整性验证失败：{0}。
Le téléchargement sera refait. Si cela persiste, lancez Vérifier les fichiers.|The file will be downloaded again. If this persists, run Verify files.|将重新下载文件。如果问题持续存在，请验证文件。
Fichier introuvable sur le serveur|File missing from the server|服务器上缺少文件
Un fichier annoncé par le catalogue est absent du serveur de téléchargement : {0}.|A file listed in the catalogue is missing from the download server: {0}.|下载服务器缺少文件列表中的文件：{0}。
Une mise à jour est probablement en cours de publication. Réessayez dans quelques minutes.|An update may be being published. Try again in a few minutes.|可能正在发布更新，请几分钟后重试。
Mise à jour refusée par sécurité|Update blocked for security|出于安全原因已阻止更新
Cette mise à jour ne sera pas appliquée. Prévenez l'équipe COTK avec ce code.|This update will not be applied. Send this code to the COTK team.|不会应用此更新。请将此代码发送给 COTK 团队。
Jeu non installé|Game not installed|游戏未安装
Le client du jeu n'est pas installé (ou pas à jour).|The game client is not installed or is out of date.|游戏客户端尚未安装或不是最新版本。
Ouvrez Bibliothèque pour l'installer.|Open Library to install it.|请打开游戏库进行安装。
Le jeu est déjà ouvert|The game is already running|游戏已经在运行
Une instance de H1Z1 tourne déjà.|An instance of H1Z1 is already running.|H1Z1 已有一个实例在运行。
Fermez-la avant de relancer.|Close it before launching again.|请关闭后再重新启动。
Serveur de jeu hors ligne|Game server offline|游戏服务器离线
Les serveurs de COTK ne répondent pas pour le moment.|COTK servers are currently not responding.|COTK 服务器暂时没有响应。
Réessayez dans quelques minutes (l'état est affiché en haut à droite).|Try again in a few minutes. Service status is shown at the top right.|请几分钟后重试，右上角会显示服务状态。
Le jeu se ferme au démarrage|The game crashes on startup|游戏启动时崩溃
Le client a planté {0} fois de suite{1}.|The client crashed {0} times in a row{1}.|客户端连续崩溃 {0} 次{1}。
1) Bibliothèque › Vérifier les fichiers · 2) Ajoutez le dossier du jeu aux exclusions de votre antivirus · 3) Mettez à jour vos pilotes graphiques. Joignez le rapport si le problème persiste.|1) Library › Verify files · 2) Add the game folder to your antivirus exclusions · 3) Update your graphics drivers. Attach a report if the issue persists.|1）游戏库 › 验证文件 · 2）将游戏文件夹加入杀毒软件排除列表 · 3）更新显卡驱动。若问题持续存在，请附上诊断报告。
Session de jeu refusée|Game session rejected|游戏会话被拒绝
Le serveur de jeu a refusé votre session de connexion.|The game server rejected your sign-in session.|游戏服务器拒绝了您的登录会话。
Reconnectez-vous au launcher puis relancez. Si cela persiste, exportez le rapport.|Sign in to the launcher again and retry. If this persists, export a report.|请重新登录启动器后再试。如果问题持续存在，请导出报告。
Le chargement du monde ne se termine pas|World loading did not finish|世界加载未完成
Le jeu est resté bloqué sur l'écran de chargement.|The game got stuck on the loading screen.|游戏卡在加载画面上。
Réessayez. Si le serveur est chargé, patientez quelques minutes.|Try again. If the server is busy, wait a few minutes.|请重试。如果服务器繁忙，请等待几分钟。
H1Z1.exe introuvable|H1Z1.exe not found|找不到 H1Z1.exe
Le fichier est absent : {0}|The file is missing: {0}|缺少文件：{0}
Lancez Vérifier les fichiers dans Bibliothèque.|Run Verify files in Library.|请在游戏库中验证文件。
Impossible de démarrer le jeu|Could not start the game|无法启动游戏
Windows a refusé de lancer le client.|Windows refused to launch the client.|Windows 拒绝启动客户端。
Vérifiez que l'antivirus n'a pas mis H1Z1.exe en quarantaine.|Check that your antivirus has not quarantined H1Z1.exe.|请检查杀毒软件是否隔离了 H1Z1.exe。
Erreur inattendue|Unexpected error|意外错误
Quelque chose s'est mal passé dans le launcher.|Something went wrong in the launcher.|启动器发生错误。
Réessayez. Si cela se reproduit, exportez le rapport et envoyez-le au support.|Try again. If it happens again, export a report and send it to support.|请重试。如果再次发生，请导出报告并发送给支持团队。
Configuration invalide|Invalid configuration|配置无效
Réinstallez le launcher depuis cotk.fr.|Reinstall the launcher from cotk.fr.|请从 cotk.fr 重新安装启动器。
À l'instant|Just now|刚刚
Il y a {0} min|{0} min ago|{0} 分钟前
Il y a {0} h|{0} h ago|{0} 小时前
Hier|Yesterday|昨天
Il y a {0} jours|{0} days ago|{0} 天前
Connexion au serveur…|Connecting to the server…|正在连接服务器…
Réduire|Minimize|最小化
Fermer|Close|关闭
Services disponibles|Services available|服务可用
Services injoignables|Services unreachable|服务无法连接
Vérification des services…|Checking services…|正在检查服务…
Fermer le launcher ?|Close the launcher?|关闭启动器？
Interrompre l'opération ?|Stop the operation?|停止操作？
Le jeu continuera, mais sans le launcher votre session ne sera pas renouvelée au retour au lobby : vous devrez le relancer pour rejouer.|The game will keep running, but your session will not renew when returning to the lobby without the launcher. You will need to reopen it to play again.|游戏会继续运行，但没有启动器时，返回大厅后会话不会更新。您需要重新打开启动器才能再次游玩。
Le téléchargement s'arrête. Il reprendra exactement où il en était à la prochaine ouverture.|The download will stop and resume from the same point next time.|下载将停止，并在下次打开时从同一位置继续。
Fermer quand même|Close anyway|仍然关闭
Rester|Stay|继续停留
Voir|View|查看
Détails techniques|Technical details|技术详情
Copier le rapport|Copy report|复制报告
Exporter|Export|导出
Envoyer au support|Send to support|发送给支持团队
Notes de patch|Patch notes|更新说明
Envoyer un extrait au support|Send an excerpt to support|向支持团队发送日志片段
Seul le texte affiché ci-dessous sera envoyé. Les secrets, e-mails et chemins utilisateur sont masqués. Conservation : 7 jours.|Only the text below will be sent. Secrets, emails and user paths are redacted. Retention: 7 days.|仅发送下方显示的文本。敏感信息、电子邮箱和用户路径已脱敏。保存期限：7 天。
Inclure les journaux récents du jeu|Include recent game logs|包含最近的游戏日志
J’accepte d’envoyer cet extrait à l’équipe COTK (suppression après 7 jours).|I agree to send this excerpt to the COTK team (deleted after 7 days).|我同意将此日志片段发送给 COTK 团队（7 天后删除）。
Envoyer l’extrait|Send excerpt|发送片段
Copier|Copy|复制
Préparation de l’extrait…|Preparing the excerpt…|正在准备日志片段…
Aucun journal disponible.|No logs available.|没有可用日志。
{0} source(s) · {1} caractères · extrait limité en taille|{0} source(s) · {1} characters · size-limited excerpt|{0} 个来源 · {1} 个字符 · 已限制片段大小
· Connectez-vous pour envoyer.|· Sign in to send.|· 请登录后发送。
{0} lignes max.|Up to {0} lines|最多 {0} 行
Rapport reçu : {0}|Report received: {0}|已收到报告：{0}
Suppression : {0}. Communiquez ce numéro au support.|Deletion: {0}. Give this reference number to support.|删除时间：{0}。请将此编号提供给支持团队。
Copier le numéro|Copy reference number|复制编号
Rapports reçus · support|Received reports · support|已收到的报告 · 支持
Accès réservé au staff. Les rapports expirent après sept jours.|Staff access only. Reports expire after seven days.|仅工作人员可以访问，报告七天后过期。
Mode aperçu : aucun rapport réel n’est chargé.|Preview mode: no real reports are loaded.|预览模式：不会加载任何真实报告。
Aucun rapport actif.|No active reports.|没有未过期的报告。
Chargement…|Loading…|正在加载…
Rapport {0} · expire le {1}|Report {0} · expires {1}|报告 {0} · 到期时间 {1}
Afficher / masquer|Show / hide|显示 / 隐藏
Partie terminée.|Game closed.|游戏已关闭。
Jeu fermé.|Game closed.|游戏已关闭。
Le client a rencontré un problème — nouvelle tentative {0}/{1}…|The client encountered a problem — retrying {0}/{1}…|客户端遇到问题——正在重试 {0}/{1}…
Client {0} à jour.|Client {0} is up to date.|客户端 {0} 已是最新版本。
Vérification terminée.|Verification complete.|验证完成。
Lecture du catalogue de fichiers…|Reading the file catalogue…|正在读取文件列表…
Le catalogue de fichiers ne correspond pas à la version annoncée.|The file catalogue does not match the announced version.|文件列表与公布的版本不一致。
Entrée de catalogue invalide : {0}|Invalid catalogue entry: {0}|文件列表条目无效：{0}
Analyse des fichiers… {0}/{1}|Scanning files… {0}/{1}|正在分析文件… {0}/{1}
Nettoyage des fichiers obsolètes…|Cleaning up obsolete files…|正在清理过期文件…
Téléchargement… {0}/{1} fichiers|Downloading… {0}/{1} files|正在下载… {0}/{1} 个文件
Connexion interrompue — reprise dans {0} s ({1}/{2})|Connection interrupted — resuming in {0} s ({1}/{2})|连接中断——将在 {0} 秒后继续（{1}/{2}）
L'archive publiée ({0}) ne correspond pas au manifeste ({1}).|The published archive ({0}) does not match the manifest ({1}).|已发布的压缩包（{0}）与清单（{1}）不一致。
L'archive reçue est plus grosse qu'annoncé.|The received archive is larger than announced.|收到的压缩包大于公布的大小。
L'archive ne contient pas {0} à sa racine.|The archive does not contain {0} at its root.|压缩包根目录中没有 {0}。
Installation…|Installing…|正在安装…
Client {0} installé.|Client {0} installed.|客户端 {0} 已安装。
L'archive contient un nom de fichier invalide.|The archive contains an invalid filename.|压缩包包含无效文件名。
L'archive contient un chemin de sortie invalide.|The archive contains an unsafe extraction path.|压缩包包含不安全的解压路径。
Chemin hors du dossier du jeu : {0}|Path outside the game folder: {0}|路径位于游戏文件夹之外：{0}
Un lien symbolique ou une jonction a été trouvé dans le dossier du jeu.|A symbolic link or junction was found in the game folder.|在游戏文件夹中发现符号链接或目录联接。
L'adresse de l'API doit être en HTTPS.|The API address must use HTTPS.|API 地址必须使用 HTTPS。
Adresse de validation du compte non autorisée.|Unauthorized account verification address.|账号验证地址未获授权。
Extrait non envoyé|Excerpt not sent|日志片段未发送
Choisissez un extrait et confirmez son envoi.|Select an excerpt and confirm sending it.|请选择日志片段并确认发送。
Envoi indisponible|Upload unavailable|发送服务不可用
Le service de rapports n'est pas encore disponible. Vous pouvez exporter le diagnostic localement.|The report service is not available yet. You can export diagnostics locally.|报告服务尚不可用，您可以在本地导出诊断报告。
Extrait trop volumineux|Excerpt too large|日志片段过大
Choisissez moins de lignes puis réessayez.|Select fewer lines and retry.|请选择更少的行后重试。
Version de manifeste non prise en charge : {0}.|Unsupported manifest version: {0}.|不支持的清单版本：{0}。
Le chemin de l'exécutable du client est invalide.|The client executable path is invalid.|客户端可执行文件路径无效。
Version {0} invalide : {1}.|Invalid {0} version: {1}.|{0} 版本无效：{1}。
Empreinte ou taille invalide pour la release {0} {1}.|Invalid hash or size for release {0} {1}.|版本 {0} {1} 的哈希值或大小无效。
URL absente pour la release {0} {1}.|Missing URL for release {0} {1}.|版本 {0} {1} 缺少下载地址。
Adresse de téléchargement invalide.|Invalid download address.|下载地址无效。
Un téléchargement doit utiliser HTTPS.|Downloads must use HTTPS.|下载必须使用 HTTPS。
La signature ou la validité de la publication est incorrecte. La mise à jour est refusée.|The release signature or validity is invalid. The update was refused.|发布包的签名或有效期不正确，已拒绝更新。
L'empreinte du catalogue ne correspond pas à la publication signée.|The catalogue fingerprint does not match the signed release.|文件目录指纹与已签名的发布不一致。
Catalogue trop volumineux.|Catalogue too large.|文件目录过大。
Installer|Install|安装
Réinstaller|Reinstall|重新安装
Staff|Staff|工作人员
Console F8 staff|Staff F8 console|工作人员 F8 控制台
Console F8 staff disponible|Staff F8 console available|工作人员 F8 控制台可用
Mise à jour de la console F8|F8 console update|F8 控制台更新
Elle s'ouvrira en jeu avec la touche F8. Installation en un clic, vérifiée par SHA-256.|It opens in game with the F8 key. One-click install, verified by SHA-256.|在游戏中按 F8 打开。一键安装，并通过 SHA-256 校验。
Console F8 installée|F8 console installed|F8 控制台已安装
Elle s'ouvrira au prochain lancement du jeu.|It will open the next time the game starts.|将在下次启动游戏时可用。
Fermez le jeu d'abord|Close the game first|请先关闭游戏
La console est utilisée pendant la partie. Installez-la quand le jeu est fermé.|The console is in use during a match. Install it when the game is closed.|控制台在游戏期间被使用。请在游戏关闭后安装。
Installer la console F8|Install the F8 console|安装 F8 控制台
Mettre à jour la console F8|Update the F8 console|更新 F8 控制台
Installée et à jour. Elle s'ouvre en jeu avec la touche F8.|Installed and up to date. It opens in game with the F8 key.|已安装且为最新版本。在游戏中按 F8 打开。
Non installée. Téléchargement depuis l'API réservée au staff, vérifié par SHA-256.|Not installed. Downloaded from the staff-only API and verified by SHA-256.|未安装。从仅限工作人员的 API 下载，并通过 SHA-256 校验。
Une version plus récente est disponible.|A newer version is available.|有更新的版本可用。
Téléchargement et vérification en cours…|Downloading and verifying…|正在下载并校验…
Vérification de la version disponible…|Checking the available version…|正在检查可用版本…
Console indisponible sur le serveur pour le moment.|Console unavailable on the server right now.|控制台目前在服务器上不可用。
Console F8 réservée au staff.|F8 console reserved for staff.|F8 控制台仅限工作人员。
La taille de la console reçue ne correspond pas à celle annoncée.|The received console size does not match the announced size.|收到的控制台大小与公布的大小不一致。
La console reçue est plus grosse qu'annoncé.|The received console is larger than announced.|收到的控制台比公布的更大。
La console reçue est incomplète.|The received console is incomplete.|收到的控制台不完整。
Mise à jour du launcher|Launcher update|启动器更新
Une nouvelle version du launcher est disponible. Il va se fermer puis se relancer tout seul. Vos réglages et le jeu ne sont pas touchés.|A new launcher version is available. It will close and restart by itself. Your settings and the game are not touched.|有新版本启动器。它将自动关闭并重新启动。你的设置和游戏不会受到影响。
Plus tard|Later|稍后
Version {0} → {1}|Version {0} → {1}|版本 {0} → {1}
Terminez d'abord l'opération en cours|Finish the current operation first|请先完成当前操作
La mise à jour du launcher redémarre l'application. Réessayez quand l'installation ou la partie est terminée.|The launcher update restarts the application. Try again when the installation or match is finished.|启动器更新会重启应用。请在安装或对局结束后重试。
Téléchargement de la mise à jour…|Downloading the update…|正在下载更新…
Launcher {0}|Launcher {0}|启动器 {0}
Redémarrage du launcher…|Restarting the launcher…|正在重启启动器…
Mise à jour du launcher disponible : {0}|Launcher update available: {0}|有启动器更新：{0}
La signature ou la validité du paquet est incorrecte.|The package signature or validity is invalid.|软件包的签名或有效期不正确。
La taille du paquet ne correspond pas à celle publiée.|The package size does not match the published size.|软件包大小与公布的大小不一致。
Le paquet reçu est plus gros que prévu.|The received package is larger than expected.|收到的软件包比预期更大。
Le paquet contient un dossier réservé aux données du joueur.|The package contains a folder reserved for player data.|软件包包含为玩家数据保留的文件夹。
La publication signée est trop volumineuse.|The signed release is too large.|已签名的发布文件过大。
Hôte de téléchargement non autorisé : {0}.|Unauthorized download host: {0}.|下载主机未获授权：{0}。
APERÇU|PREVIEW|预览
DONNÉES FICTIVES|SAMPLE DATA|模拟数据
LAUNCHER {0} · COTK.FR · APERÇU / DONNÉES FICTIVES|LAUNCHER {0} · COTK.FR · PREVIEW / SAMPLE DATA|启动器 {0} · COTK.FR · 预览 / 模拟数据
v{0} · APERÇU|v{0} · PREVIEW|v{0} · 预览
Continuer|Continue|继续
Langue du launcher : {0}|Launcher language: {0}|启动器语言：{0}
Mode aperçu|Preview mode|预览模式
Notes de patch · {0}|Patch notes · {0}|更新说明 · {0}
{0} h {1} min|{0} h {1} min|{0} 小时 {1} 分钟
{0} min {1} s|{0} min {1} s|{0} 分钟 {1} 秒
{0} s|{0} s|{0} 秒
Patch Alpha V0.1 — Classement et progression|Alpha V0.1 — Ranking and progression|Alpha V0.1——排位与进度
Serveurs de nouveau en ligne|Servers back online|服务器已恢复上线
Saison 3 : le classement démarre|Season 3: ranked play begins|第 3 赛季：排位赛开启
Patch Alpha V0 : mise à jour du gameplay|Alpha V0: gameplay update|Alpha V0：游戏更新
La maintenance prolongée est terminée sur toutes les régions.|Extended maintenance has ended in all regions.|所有地区的延长维护已经结束。
Merci pour votre patience !|Thank you for your patience!|感谢您的耐心等待！
Nouveau palier de récompenses et rotation de carte mise à jour.|New reward tiers and an updated map rotation.|新的奖励等级和地图轮换已更新。
Bonne chance à tous les survivants.|Good luck to all survivors.|祝所有幸存者好运。
Changement d'arme plus propre, +50 % de butin au sol, visages et coiffures respectés à la création.|Smoother weapon swaps, 50% more ground loot, and character faces and hairstyles preserved.|切换武器更流畅、地面战利品增加 50%，并保留所选角色面容和发型。
🏆 Classement et progression|🏆 Ranking and progression|🏆 排位与进度
Score de match : chaque partie classée rapporte un score.|Match scores: every ranked match awards points.|对局积分：每场排位对局都会获得积分。
Top 10 persistant : vos meilleurs scores sont conservés par compte.|Persistent Top 10: your best scores are saved to your account.|保留最佳 10 场成绩：最高积分会保存到您的账号。
Badge de rang dans le killfeed.|Rank badges in the killfeed.|击杀信息中显示段位徽章。
🧥 Apparence et équipement|🧥 Appearance and equipment|🧥 外观与装备
Les équipements ramassés s'affichent correctement sur le personnage.|Picked-up equipment now displays correctly on your character.|拾取的装备现在会正确显示在角色身上。
Retirer ou jeter un vêtement met l'apparence à jour immédiatement.|Removing or dropping clothing updates your appearance immediately.|移除或丢弃服装会立即更新角色外观。
⚔️ Combat|⚔️ Combat|⚔️ 战斗
Les morts par saignement sont créditées au dernier attaquant.|Bleeding deaths are credited to the last attacker.|流血造成的击杀归于最后一位攻击者。
Les cibles d'entraînement n'augmentent plus le compteur d'éliminations.|Practice targets no longer increase your kill count.|训练靶不再增加击杀计数。
non installé|Not installed|未安装
(code {0})|(code {0})|（错误码 {0}）
locale|local|本地
Launcher {0} · client {1} · API {2}|Launcher {0} · client {1} · API {2}|启动器 {0} · 客户端 {1} · API {2}
""";
}
