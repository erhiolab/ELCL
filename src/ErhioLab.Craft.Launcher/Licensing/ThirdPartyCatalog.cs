using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Licensing;

/// <summary>启动器使用的组件、参考项目、资源与服务, 与玩家安装的游戏内容分开列出.</summary>
internal static class ThirdPartyCatalog
{
    internal sealed record Entry(string Id, string Name, string Description, string SourceUrl, string LicenseUrl);
    internal sealed record Group(string Title, IReadOnlyList<Entry> Entries);

    internal static IReadOnlyList<Group> Create()
    {
        string L(string key) => LocalizationManager.Get("about.licenses." + key);
        Entry Item(string id, string name, string source, string license) => new(id, name, L(id), source, license);
        return
        [
            new Group(L("components"),
            [
                Item("elcl", "ErhioLab Craft Launcher", "https://github.com/ErhioLab/ELCL", "https://github.com/ErhioLab/ELCL/blob/main/LICENSE"),
                Item("elclc", "ErhioLab Craft Launcher Core", "https://github.com/ErhioLab/ELCL/tree/main/src/ErhioLab.Craft.Launcher.Core", "https://github.com/ErhioLab/ELCL/blob/main/LICENSE"),
                Item("dotnet", ".NET", "https://github.com/dotnet/runtime", "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"),
                Item("dotnetNotices", ".NET Third-Party Notices", "https://github.com/dotnet/runtime", "https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT"),
                Item("wpf", "Windows Presentation Foundation", "https://github.com/dotnet/wpf", "https://github.com/dotnet/wpf/blob/main/LICENSE.TXT"),
                Item("magicScaler", "PhotoSauce.MagicScaler", "https://github.com/saucecontrol/PhotoSauce", "https://github.com/saucecontrol/PhotoSauce/blob/9af284f48de413e52e7b7aa7076fcaa3bc0d9d7c/license"),
                Item("nativeWebp", "PhotoSauce.NativeCodecs.Libwebp", "https://github.com/saucecontrol/PhotoSauce", "https://github.com/saucecontrol/PhotoSauce/blob/3f419c14390c3c2e7cd29a271450ed1ce2daafac/license"),
                Item("libwebp", "libwebp", "https://github.com/webmproject/libwebp", "https://github.com/webmproject/libwebp/blob/v1.6.0/COPYING"),
                Item("photoSauceNotices", "PhotoSauce Third-Party Notices", "https://github.com/saucecontrol/PhotoSauce", "https://github.com/saucecontrol/PhotoSauce/blob/3f419c14390c3c2e7cd29a271450ed1ce2daafac/third-party-notices")
            ]),
            new Group(L("references"),
            [
                Item("javaWrapper", "Java Launch Wrapper", "https://github.com/00ll00/java_launch_wrapper", "https://github.com/00ll00/java_launch_wrapper/blob/master/LICENCE"),
                Item("lwjglAgent", "LWJGL Unsafe Agent", "https://github.com/HMCL-dev/lwjgl-unsafe-agent", "https://github.com/HMCL-dev/lwjgl-unsafe-agent/blob/main/LICENSE"),
                Item("pcl", "PCL", "https://github.com/Meloong-Git/PCL", "https://github.com/Meloong-Git/PCL/blob/main/LICENCE"),
                Item("prism", "Prism Launcher", "https://github.com/PrismLauncher/PrismLauncher", "https://github.com/PrismLauncher/PrismLauncher/blob/develop/COPYING.md"),
                Item("hmcl", "HMCL", "https://github.com/HMCL-dev/HMCL", "https://github.com/HMCL-dev/HMCL/blob/main/LICENSE"),
                Item("prismLogo", L("prismLogoName"), "https://github.com/PrismLauncher/PrismLauncher", "https://github.com/PrismLauncher/PrismLauncher/blob/develop/COPYING.md"),
                Item("creditImages", L("creditImagesName"), "https://github.com/ErhioLab/ELCL/tree/main/src/ErhioLab.Craft.Launcher/Assets/Credits", "https://github.com/ErhioLab/ELCL/blob/main/src/ErhioLab.Craft.Launcher/Assets/Credits/README.md")
            ]),
            new Group(L("resources"),
            [
                Item("mcmod", LocalizationManager.Get("about.mcmodName"), "https://www.mcmod.cn/", "https://github.com/ErhioLab/ELCL/blob/main/src/ErhioLab.Craft.Launcher.Core/Mods/Data/NOTICE.md"),
                Item("minecraftWiki", "Minecraft Wiki", "https://zh.minecraft.wiki/", "https://zh.minecraft.wiki/w/Minecraft_Wiki:%E5%85%B3%E4%BA%8E")
            ]),
            new Group(L("services"),
            [
                Item("microsoft", "Microsoft / Xbox", "https://learn.microsoft.com/entra/identity-platform/", "https://www.microsoft.com/servicesagreement"),
                Item("minecraft", "Minecraft / Mojang", "https://www.minecraft.net/", "https://www.minecraft.net/eula"),
                Item("curseforge", "CurseForge", "https://www.curseforge.com/", "https://legal.overwolf.com/docs/overwolf/platform/platform-terms-of-use/"),
                Item("modrinth", "Modrinth", "https://modrinth.com/", "https://modrinth.com/legal/terms"),
                Item("bmclapi", "BMCLAPI", "https://bmclapi2.bangbang93.com/", "https://bmclapidoc.bangbang93.com/"),
                Item("mcim", "MCIM", "https://github.com/mcmod-info-mirror/mcim-api", "https://github.com/mcmod-info-mirror/mcim-api/blob/main/LICENSE"),
                Item("posthog", "PostHog", "https://posthog.com/", "https://posthog.com/terms"),
                Item("adoptium", "Eclipse Adoptium", "https://adoptium.net/", "https://adoptium.net/docs/faq/"),
                Item("azul", "Azul Zulu", "https://www.azul.com/", "https://www.azul.com/legal/"),
                Item("semeru", "IBM Semeru", "https://developer.ibm.com/languages/semeru-runtimes/", "https://www.ibm.com/support/customer/csol/terms/")
            ])
        ];
    }
}
