using System.Net;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Mods;

/// <summary>社区 Mods 查询与依赖计划; API Key、HTTP 客户端及来源偏好由调用方提供.</summary>
public sealed class ModRepositoryClient
{
    private readonly HttpClient _http;
    private readonly string? _key;
    private readonly CommunitySource _source;

    public ModRepositoryClient(HttpClient httpClient, string? curseForgeApiKey = null, CommunitySource source = CommunitySource.OfficialFirst)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _key = curseForgeApiKey;
        _source = source;
    }

    /// <summary>按相关性或下载量合并来源前缀后分页; 单来源失败时返回另一来源与失败标记.</summary>
    public async Task<ModSearchResult> SearchAsync(ModSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Page is < 0 or > 499 || request.PageSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(request));
        if (!Enum.IsDefined(request.SortOrder)) throw new ArgumentOutOfRangeException(nameof(request));
        var platforms = request.Platform == ModPlatform.All ? new[] { ModPlatform.Modrinth, ModPlatform.CurseForge } : [request.Platform];
        int required = checked((request.Page + 1) * request.PageSize + 1);
        var results = await Task.WhenAll(platforms.Select(QuerySource)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (results.All(result => result.Error is not null)) throw new AggregateException("Mods 来源均不可用.", results.Select(result => result.Error!));
        var ranked = results.SelectMany(result => result.Projects.Select((project, rank) => (Project: project, Rank: rank)))
            .DistinctBy(item => (item.Project.Platform, item.Project.Id));
        // 相关性分数不跨平台比较, 按各来源名次交替合并, 保留来源内部顺序.
        var ordered = (request.SortOrder == ModSortOrder.Downloads
                ? ranked.OrderByDescending(item => item.Project.Downloads).ThenBy(item => item.Project.Id, StringComparer.Ordinal)
                : ranked.OrderBy(item => item.Rank))
            .Select(item => item.Project).ToArray();
        return new ModSearchResult(ordered.Skip(request.Page * request.PageSize).Take(request.PageSize).ToArray(), results.Sum(result => result.Total),
            ordered.Length > (request.Page + 1) * request.PageSize, results.Where(result => result.Error is not null).Select(result => result.Platform).ToArray())
        {
            SourceErrors = results.Where(result => result.Error is not null).ToDictionary(result => result.Platform, result => result.Error!)
        };

        async Task<(ModPlatform Platform, IReadOnlyList<ModProject> Projects, long Total, Exception? Error)> QuerySource(ModPlatform platform)
        {
            try
            {
                var result = await SearchSourceAsync(platform, request, required, cancellationToken).ConfigureAwait(false);
                return (platform, result.Projects, result.Total, null);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or TaskCanceledException)
            {
                return (platform, [], 0, error);
            }
        }
    }

    private async Task<(IReadOnlyList<ModProject> Projects, long Total)> SearchSourceAsync(ModPlatform platform, ModSearchRequest search, int required, CancellationToken token)
    {
        List<ModProject> projects = [];
        long total = long.MaxValue;
        for (int offset = 0; offset < required && offset < total;)
        {
            if (platform == ModPlatform.CurseForge && offset >= 10000) break;
            int count = Math.Min(50, required - offset);
            if (platform == ModPlatform.CurseForge) count = Math.Min(count, 10000 - offset);
            string path;
            if (platform == ModPlatform.Modrinth)
            {
                if (search.Category is { ModrinthTag.Length: 0 }) return ([], 0);
                List<string[]> facets = [["project_type:mod"]];
                if (!string.IsNullOrWhiteSpace(search.GameVersion)) facets.Add(["versions:" + search.GameVersion]);
                if (!string.IsNullOrWhiteSpace(search.Loader)) facets.Add(["categories:" + search.Loader.ToLowerInvariant()]);
                if (search.Category is { ModrinthTag.Length: > 0 } category) facets.Add(["categories:" + category.ModrinthTag]);
                string order = search.SortOrder == ModSortOrder.Downloads ? "downloads" : "relevance";
                path = $"/v2/search?index={order}&limit={count}&offset={offset}&query={Escape(search.Query)}&facets={Escape(JsonSerializer.Serialize(facets))}";
            }
            else
            {
                if (search.Category is { CurseForgeId: null }) return ([], 0);
                int order = search.SortOrder == ModSortOrder.Downloads ? 6 : 1;
                path = $"/v1/mods/search?gameId=432&classId=6&sortField={order}&sortOrder=desc&pageSize={count}&index={offset}&searchFilter={Escape(search.Query)}";
                if (!string.IsNullOrWhiteSpace(search.GameVersion)) path += "&gameVersion=" + Escape(search.GameVersion);
                if (LoaderId(search.Loader) is { } loader) path += "&modLoaderType=" + loader;
                if (search.Category?.CurseForgeId is { } category) path += "&categoryId=" + category;
            }

            using var doc = await RequestAsync(platform, path, null, token).ConfigureAwait(false);
            var root = doc.RootElement;
            var rows = root.GetProperty(platform == ModPlatform.Modrinth ? "hits" : "data").EnumerateArray().ToArray();
            total = platform == ModPlatform.Modrinth ? root.GetProperty("total_hits").GetInt64() : root.GetProperty("pagination").GetProperty("totalCount").GetInt64();
            projects.AddRange(rows.Select(row => platform == ModPlatform.Modrinth ? ReadModrinthProject(row, true) : ReadCurseForgeProject(row)));
            offset += rows.Length;
            if (rows.Length == 0) break;
        }

        return (projects, total);
    }

    public async Task<ModProject> GetProjectAsync(ModPlatform platform, string id, CancellationToken cancellationToken = default)
    {
        ValidatePlatform(platform);
        using var doc = await RequestAsync(platform, platform == ModPlatform.Modrinth ? "/v2/project/" + Escape(id) : "/v1/mods/" + Escape(id), null, cancellationToken).ConfigureAwait(false);
        return platform == ModPlatform.Modrinth ? ReadModrinthProject(doc.RootElement, false) : ReadCurseForgeProject(doc.RootElement.GetProperty("data"));
    }

    public async Task<ModFile> GetFileAsync(ModPlatform platform, string projectId, string fileId, CancellationToken cancellationToken = default)
    {
        ValidatePlatform(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        using var document = await RequestAsync(platform, platform == ModPlatform.Modrinth ? "/v2/version/" + Escape(fileId)
            : "/v1/mods/" + Escape(projectId) + "/files/" + Escape(fileId), null, cancellationToken).ConfigureAwait(false);
        var file = platform == ModPlatform.Modrinth ? ReadModrinthFile(document.RootElement) : ReadCurseForgeFile(document.RootElement.GetProperty("data"), true);
        if (file.ProjectId != projectId || file.Id != fileId) throw new InvalidDataException("Mods 文件与请求的项目或文件标识不匹配.");
        return file;
    }

    public async Task<ModFilePage> GetFilesAsync(ModProject project, string? gameVersion = null, string? loader = null,
        int page = 0, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidatePlatform(project.Platform);
        if (page is < 0 or > 499 || pageSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(page));
        if (project.Platform == ModPlatform.Modrinth)
        {
            var all = await GetModrinthFilesAsync(project, gameVersion, loader, cancellationToken).ConfigureAwait(false);
            return new ModFilePage(all.Skip(page * pageSize).Take(pageSize).ToArray(), all.Length > (long)(page + 1) * pageSize);
        }

        if (page * pageSize >= 10000) return new ModFilePage([], false);
        string path = $"/v1/mods/{Escape(project.Id)}/files?index={page * pageSize}&pageSize={pageSize}";
        if (gameVersion is not null) path += "&gameVersion=" + Escape(gameVersion);
        if (LoaderId(loader) is { } id) path += "&modLoaderType=" + id;
        using var response = await RequestAsync(project.Platform, path, null, cancellationToken).ConfigureAwait(false);
        var files = response.RootElement.GetProperty("data").EnumerateArray().Select(row => ReadCurseForgeFile(row, project.CanDownload)).OrderByDescending(file => file.Published).ToArray();
        return new ModFilePage(files, response.RootElement.TryGetProperty("pagination", out var pagination)
            ? Math.Min(10000, pagination.GetProperty("totalCount").GetInt64()) > (long)(page + 1) * pageSize
            : files.Length == pageSize && (page + 1) * pageSize < 10000);
    }

    /// <summary>优先一次获取完整元数据; 来源返回分页时, 最多四个并行请求补齐.</summary>
    public async Task<IReadOnlyList<ModFile>> GetAllFilesAsync(ModProject project, string? gameVersion = null, string? loader = null,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidatePlatform(project.Platform);
        cancellationToken.ThrowIfCancellationRequested();
        if (project.Platform == ModPlatform.Modrinth)
        {
            var files = await GetModrinthFilesAsync(project, gameVersion, loader, cancellationToken).ConfigureAwait(false);
            progress?.Report(files.Length);
            return files;
        }

        List<ModFile> result = [];
        HashSet<string> seen = [];
        ModFilePage first;
        try
        {
            string path = $"/v1/mods/{Escape(project.Id)}/files?index=0&pageSize=10000";
            if (gameVersion is not null) path += "&gameVersion=" + Escape(gameVersion);
            if (LoaderId(loader) is { } id) path += "&modLoaderType=" + id;
            using var response = await RequestAsync(project.Platform, path, null, cancellationToken).ConfigureAwait(false);
            var files = response.RootElement.GetProperty("data").EnumerateArray().Select(row => ReadCurseForgeFile(row, project.CanDownload)).ToArray();
            bool hasMore = response.RootElement.TryGetProperty("pagination", out var pagination)
                ? Math.Min(10000, pagination.GetProperty("totalCount").GetInt64()) > files.Length
                : files.Length == 50;
            first = new ModFilePage(files, hasMore);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest)
        {
            first = await GetFilesAsync(project, gameVersion, loader, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (first.HasMore && first.Files.Count < 50)
            first = await GetFilesAsync(project, gameVersion, loader, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (first.HasMore && first.Files.Count == 0) throw new InvalidDataException("Mods 来源没有返回第一页文件.");
        Add(first);
        for (int offset = Math.Max(1, first.Files.Count / 50); first.HasMore && offset < 200; offset += 4)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pages = await Task.WhenAll(Enumerable.Range(offset, Math.Min(4, 200 - offset))
                .Select(page => GetFilesAsync(project, gameVersion, loader, page, cancellationToken: cancellationToken))).ConfigureAwait(false);
            int previousCount = result.Count;
            foreach (var page in pages) Add(page);
            if (pages.Any(page => !page.HasMore)) break;
            if (result.Count == previousCount) throw new InvalidDataException("Mods 来源没有返回下一页文件.");
        }

        return result.OrderByDescending(file => file.Published).ToArray();

        void Add(ModFilePage page)
        {
            foreach (var file in page.Files)
                if (seen.Add(file.Id))
                    result.Add(file);
            progress?.Report(result.Count);
        }
    }

    private async Task<ModFile[]> GetModrinthFilesAsync(ModProject project, string? gameVersion, string? loader, CancellationToken token)
    {
        string query = "?include_changelog=false";
        if (gameVersion is not null) query += "&game_versions=" + Escape(JsonSerializer.Serialize(new[] { gameVersion }));
        if (loader is not null) query += "&loaders=" + Escape(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() }));
        using var doc = await RequestAsync(project.Platform, "/v2/project/" + Escape(project.Id) + "/version" + query, null, token).ConfigureAwait(false);
        return doc.RootElement.EnumerateArray().Select(ReadModrinthFile).OrderByDescending(file => file.Published).ToArray();
    }

    /// <summary>按本地文件的上游指纹识别项目, 不上传文件内容.</summary>
    public async Task<ModProject?> IdentifyAsync(LocalModInfo local, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await RequestAsync(ModPlatform.Modrinth, "/v2/version_file/" + local.Sha1 + "?algorithm=sha1", null, cancellationToken).ConfigureAwait(false);
            return await GetProjectAsync(ModPlatform.Modrinth, Text(doc.RootElement, "project_id"), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
        }

        if (string.IsNullOrWhiteSpace(_key)) return null;
        using var cf = await RequestAsync(ModPlatform.CurseForge, "/v1/fingerprints/432", new { fingerprints = new[] { local.CurseForgeFingerprint } }, cancellationToken).ConfigureAwait(false);
        var match = cf.RootElement.GetProperty("data").GetProperty("exactMatches").EnumerateArray().FirstOrDefault();
        return match.ValueKind == JsonValueKind.Object ? await GetProjectAsync(ModPlatform.CurseForge, match.GetProperty("id").ToString(), cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>查询给定前置 Mods 的项目信息, 不递归解析或下载文件.</summary>
    public async Task<IReadOnlyList<ModProject>> GetDependenciesAsync(ModPlatform platform, IEnumerable<ModDependency> dependencies,
        CancellationToken cancellationToken = default)
    {
        ValidatePlatform(platform);
        ArgumentNullException.ThrowIfNull(dependencies);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = dependencies.DistinctBy(dependency => dependency.ProjectId is { Length: > 0 } id ? "project:" + id : "version:" + dependency.VersionId).Take(129).ToArray();
        if (entries.Length > 128) throw new InvalidDataException("前置 Mods 数量过多.");
        using var limit = new SemaphoreSlim(4);
        var lookups = new Task<ModProject>[entries.Length];
        for (var index = 0; index < entries.Length; index++)
            lookups[index] = ResolveDependencyAsync(platform, entries[index], limit, cancellationToken);
        var projects = await Task.WhenAll(lookups).ConfigureAwait(false);
        return projects.DistinctBy(project => project.Id).OrderByDescending(project => project.Downloads).ToArray();
    }

    private async Task<ModProject> ResolveDependencyAsync(ModPlatform platform, ModDependency dependency, SemaphoreSlim limit, CancellationToken cancellationToken)
    {
        await limit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? id = dependency.ProjectId;
            if (id is null && platform == ModPlatform.Modrinth && dependency.VersionId is { } version)
            {
                using var document = await RequestAsync(platform, "/v2/version/" + Escape(version), null, cancellationToken).ConfigureAwait(false);
                id = Text(document.RootElement, "project_id");
            }

            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("前置 Mods 缺少项目标识.");
            return await GetProjectAsync(platform, id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            limit.Release();
        }
    }

    /// <summary>递归解析必需依赖, 保留明确版本约束; 不安装可选依赖, 不写入磁盘.</summary>
    public async Task<IReadOnlyList<ModDownloadItem>> CreateDownloadPlanAsync(ModProject project, ModFile file, string? gameVersion = null,
        string? loader = null, CancellationToken cancellationToken = default)
    {
        List<ModDownloadItem> result = [];
        Dictionary<(ModPlatform, string), string> visited = [];
        await Add(project, file).ConfigureAwait(false);
        return result;

        async Task Add(ModProject current, ModFile selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited.TryGetValue((current.Platform, current.Id), out var previous))
            {
                if (previous != selected.Id) throw new InvalidDataException("必需依赖存在版本冲突.");
                return;
            }

            if (visited.Count >= 128) throw new InvalidDataException("Mods 依赖数量过多.");
            if (selected.Platform != current.Platform || selected.ProjectId != current.Id) throw new ArgumentException("所选文件不属于当前 Mods.", nameof(file));
            if (gameVersion is not null && !selected.GameVersions.Contains(gameVersion) || loader is not null && !selected.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("所选 Mods 与游戏版本或加载器不兼容: " + current.Name);
            if (!current.CanDownload || selected.DownloadUri is null) throw new InvalidOperationException("作者限制了第三方下载, 请前往来源页面.");
            visited[(current.Platform, current.Id)] = selected.Id;
            foreach (var dependency in selected.RequiredDependencies)
            {
                ModFile? dependent = null;
                string? projectId = dependency.ProjectId;
                if (current.Platform == ModPlatform.Modrinth && dependency.VersionId is { } versionId)
                {
                    using var exact = await RequestAsync(current.Platform, "/v2/version/" + Escape(versionId), null, cancellationToken).ConfigureAwait(false);
                    dependent = ReadModrinthFile(exact.RootElement);
                    projectId = dependent.ProjectId;
                }

                if (projectId is null) throw new InvalidDataException("必需依赖没有项目或文件标识.");
                var nextProject = await GetProjectAsync(current.Platform, projectId, cancellationToken).ConfigureAwait(false);
                if (dependent is null)
                {
                    var versions = await GetFilesAsync(nextProject, gameVersion, loader, cancellationToken: cancellationToken).ConfigureAwait(false);
                    dependent = versions.Files.OrderBy(version => version.ReleaseType == "release" ? 0 : 1).ThenByDescending(version => version.Published).FirstOrDefault();
                }

                if (dependent is null || gameVersion is not null && !dependent.GameVersions.Contains(gameVersion) || loader is not null && !dependent.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("没有符合当前游戏版本和加载器的必需依赖: " + nextProject.Name);
                await Add(nextProject, dependent).ConfigureAwait(false);
            }

            result.Add(new ModDownloadItem(current, selected));
        }
    }

    public static IReadOnlyList<Uri> DownloadSources(Uri official, CommunitySource source)
    {
        string? replacement = official.Host switch
        {
            "cdn.modrinth.com" or "edge.forgecdn.net" or "mediafilez.forgecdn.net" => "https://mod.mcimirror.top",
            _ => null
        };
        if (replacement is null) return [official];
        var mirror = new Uri(replacement + official.PathAndQuery);
        return source == CommunitySource.MirrorFirst ? [mirror, official] : [official, mirror];
    }

    private async Task<JsonDocument> RequestAsync(ModPlatform platform, string path, object? body, CancellationToken token)
    {
        ValidatePlatform(platform);
        Exception? failure = null;
        bool[] order = _source == CommunitySource.MirrorFirst ? [true, false] : [false, true];
        foreach (bool mirror in order)
        {
            if (!mirror && platform == ModPlatform.CurseForge && string.IsNullOrWhiteSpace(_key))
            {
                failure = new InvalidOperationException("CurseForge API Key 未提供.");
                continue;
            }

            string host = mirror ? "https://mod.mcimirror.top/" + (platform == ModPlatform.Modrinth ? "modrinth" : "curseforge")
                : platform == ModPlatform.Modrinth ? "https://api.modrinth.com" : "https://api.curseforge.com";
            try
            {
                using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, host + path);
                request.Headers.UserAgent.ParseAdd("ELCLC/" + CoreInfo.Version + " (https://github.com/ErhioLab/ELCL)");
                if (!mirror && platform == ModPlatform.CurseForge) request.Headers.TryAddWithoutValidation("x-api-key", _key);
                if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 32 * 1024 * 1024) throw new InvalidDataException("Mods 元数据过大.");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var output = new MemoryStream();
                var buffer = new byte[65536];
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("Mods 元数据过大.");
                    output.Write(buffer, 0, count);
                }

                return JsonDocument.Parse(output.ToArray());
            }
            catch (HttpRequestException error) when (!mirror && error.StatusCode == HttpStatusCode.NotFound)
            {
                throw;
            }
            catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
            {
                failure = error;
            }
        }

        throw failure ?? new HttpRequestException("Mods 来源不可用.");
    }

    private static void ValidatePlatform(ModPlatform platform)
    {
        if (platform is not (ModPlatform.CurseForge or ModPlatform.Modrinth)) throw new ArgumentOutOfRangeException(nameof(platform));
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static int? LoaderId(string? loader) => loader?.ToLowerInvariant() switch { "forge" => 1, "fabric" => 4, "quilt" => 5, "neoforge" => 6, "liteloader" => 3, _ => null };
    private static string Text(JsonElement root, string key, string fallback = "") => root.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback;
    private static string[] Strings(JsonElement root, string key) => root.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Select(value => value.ToString()).ToArray() : [];

    private static long Number(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
        if (value.TryGetInt64(out var number)) return number;
        return value.TryGetDouble(out var approximate) && double.IsFinite(approximate) && approximate is >= 0 and < long.MaxValue ? (long)approximate : 0;
    }

    private static DateTimeOffset? Date(JsonElement root, string key) => DateTimeOffset.TryParse(Text(root, key), out var value) ? value : null;
    private static Uri? Url(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri : null;
    private static readonly string[] KnownLoaders = ["forge", "neoforge", "fabric", "quilt", "liteloader"];

    private static ModProject ReadModrinthProject(JsonElement row, bool search)
    {
        string id = Text(row, search ? "project_id" : "id"), slug = Text(row, "slug", id);
        var categories = Strings(row, "categories");
        return new ModProject(ModPlatform.Modrinth, id, slug, Text(row, "title"), Text(row, "description"), new Uri("https://modrinth.com/mod/" + Escape(slug)),
            Url(Text(row, "icon_url")), Number(row, "downloads"), Date(row, search ? "date_modified" : "updated"), search ? Strings(row, "versions") : Strings(row, "game_versions"),
            search ? categories.Intersect(KnownLoaders).ToArray() : Strings(row, "loaders"), categories.Except(KnownLoaders).ToArray());
    }

    private static ModProject ReadCurseForgeProject(JsonElement row)
    {
        var categories = row.TryGetProperty("categories", out var categoryArray) ? categoryArray.EnumerateArray().Select(value => Text(value, "slug")).ToArray() : [];
        var indices = row.TryGetProperty("latestFilesIndexes", out var array) ? array.EnumerateArray().ToArray() : [];
        string id = Text(row, "id"), slug = Text(row, "slug", id);
        return new ModProject(ModPlatform.CurseForge, id, slug, Text(row, "name"), Text(row, "summary"), new Uri("https://www.curseforge.com/minecraft/mc-mods/" + Escape(slug)),
            row.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object ? Url(Text(logo, "thumbnailUrl")) : null,
            Number(row, "downloadCount"), Date(row, "dateModified"), indices.Select(index => Text(index, "gameVersion")).Distinct().ToArray(),
            indices.Select(index => Number(index, "modLoader") switch { 1 => "forge", 3 => "liteloader", 4 => "fabric", 5 => "quilt", 6 => "neoforge", _ => "" }).Where(value => value.Length > 0).Distinct().ToArray(), categories,
            !row.TryGetProperty("allowModDistribution", out var allowed) || allowed.ValueKind != JsonValueKind.False);
    }

    private static ModFile ReadModrinthFile(JsonElement row)
    {
        var files = row.GetProperty("files").EnumerateArray().OrderByDescending(file => file.TryGetProperty("primary", out var primary) && primary.GetBoolean()).ToArray();
        var binary = files.FirstOrDefault(file => Text(file, "filename").EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
        if (binary.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Mods 版本没有 JAR 文件.");
        var hashes = binary.GetProperty("hashes");
        var dependencies = row.TryGetProperty("dependencies", out var deps)
            ? deps.EnumerateArray().Where(dep => Text(dep, "dependency_type") == "required")
                .Select(dep => new ModDependency(Text(dep, "project_id") is { Length: > 0 } project ? project : null, Text(dep, "version_id") is { Length: > 0 } id ? id : null)).ToArray()
            : [];
        return new ModFile(ModPlatform.Modrinth, Text(row, "project_id"), Text(row, "id"), Text(row, "name"), Text(binary, "filename"),
            Url(Text(binary, "url")), Number(binary, "size"), Text(hashes, "sha1") is { Length: > 0 } sha ? sha : null,
            Text(hashes, "sha512") is { Length: > 0 } hash ? hash : null, Date(row, "date_published"), Text(row, "version_type"), Strings(row, "game_versions"), Strings(row, "loaders"), dependencies);
    }

    private static ModFile ReadCurseForgeFile(JsonElement row, bool allowed)
    {
        string? sha = row.TryGetProperty("hashes", out var hashes) ? hashes.EnumerateArray().Where(hash => Number(hash, "algo") == 1).Select(hash => Text(hash, "value")).FirstOrDefault() : null;
        var versions = Strings(row, "gameVersions");
        var dependencies = row.TryGetProperty("dependencies", out var deps) ? deps.EnumerateArray().Where(dep => Number(dep, "relationType") == 3).Select(dep => new ModDependency(Text(dep, "modId"), null)).ToArray() : [];
        return new ModFile(ModPlatform.CurseForge, Text(row, "modId"), Text(row, "id"), Text(row, "displayName"), Text(row, "fileName"),
            allowed ? Url(Text(row, "downloadUrl")) : null, Number(row, "fileLength"), sha, null, Date(row, "fileDate"), Number(row, "releaseType") switch { 2 => "beta", 3 => "alpha", _ => "release" },
            versions.Where(version => version.Length > 0 && char.IsDigit(version[0])).ToArray(), versions.Select(version => version.ToLowerInvariant()).Intersect(KnownLoaders).ToArray(), dependencies);
    }
}
