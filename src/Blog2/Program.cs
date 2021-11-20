using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var inputDir = args[0]; // input dir
var outputDir = args[1]; // output dir

static string BuildHtml(string title, string content, string side, string footer, string? ogurl = null)
{
    var og = "";
    if (ogurl != null)
    {
        var type = (ogurl == "https://neue.cc") ? "website" : "article";
        og = @$"
<meta property=""og:url"" content=""{ogurl}"" />
<meta property=""og:type"" content=""{type}"" />
<meta property=""og:title"" content=""{title}"" />
<meta property=""og:description"" content=""{content.Substring(0, 80)}..."" />
";
    }

    return @$"<!DOCTYPE html>
<html dir=""ltr"" lang=""ja"">
<head>
    <meta charset=""utf-8"" />
    <title>{title}</title>
    <link rel=""shortcut icon"" href=""https://neue.cc/favicon.ico"" />
	<link rel=""stylesheet"" href=""https://neue.cc/style.css"" type=""text/css"" media=""screen"" />
    <link href=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.25.0/themes/prism.min.css"" rel=""stylesheet"" />
    <meta property=""og:url"" content="""" />
 </head>
<body>
	<script src=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.25.0/components/prism-core.min.js""></script>
    <script src=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.25.0/plugins/autoloader/prism-autoloader.min.js""></script>
    <script src=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.25.0/plugins/normalize-whitespace/prism-normalize-whitespace.min.js""></script>
    <div id=""wrapper"">
        <div id=""header""></div>
        <div id=""content"">{content}</div>
        <div id=""side"">{side}</div>
        <div id=""footer"">{footer}</div>
    </div>
</body>
";
}

// Load Files and create body.
var artciles = Directory.EnumerateFiles(inputDir)
    .AsParallel()
    .Select(x =>
    {
        var fileName = Path.GetFileNameWithoutExtension(x);

        var match = Regex.Match(fileName, @"(\d{4})-(\d{2})-(\d+)(.+)");
        var yyyy = match.Groups[1].Value;
        var mm = match.Groups[2].Value;
        var dd = match.Groups[3].Value;
        var dd_no = dd + match.Groups[4].Value;

        if (yyyy == "" || mm == "" || dd_no == "")
        {
            throw new Exception($"File name is not matched yyyy-MM-dd*, {Path.GetFileName(x)}");
        }

        var (first, others) = ReadAllTextWithoutFirstLine(x);
        if (!first.StartsWith("#"))
        {
            throw new Exception($".md file does not contains #title, {Path.GetFileName(x)}");
        }

        var title = first.Trim('#', ' ');
        var bodyTitle = $"<h1><a href=\"https://neue.cc/{yyyy}/{mm}/{dd_no}.html\">{title}</a></h1>";
        var bodyDate = $"<ul class=\"date\"><li>{yyyy}-{mm}-{dd}</li></ul>";
        var body = "<div class=\"entry_body\">" + Markdown.ToHtml(others) + "</div>";

        return new Article(
            Url: new PageUrl(yyyy, mm, dd_no),
            Body: bodyTitle + Environment.NewLine + bodyDate + Environment.NewLine + body,
            Title: title
        );
    })
    .WhereNotNull()
    .ToArray();

// Create Side
var sideArchives = artciles
    .GroupBy(x => (x.Url.yyyy, x.Url.mm))
    .OrderByDescending(x => x.Key)
    .Select(x => $"<li><a href=\"https://neue.cc/{x.Key.yyyy}/{x.Key.mm}/\">{x.Key.yyyy}-{x.Key.mm}</a>");

var side = $@"
<h3>Profile</h3>
<div class=""side_body"" align=""center"">
<b>Yoshifumi Kawai</b><br />
<br />
<a href=""https://cysharp.co.jp/"">Cysharp, Inc</a><br />
CEO/CTO<br />
<br />
Microsoft MVP for Developer Technologies(C#)<br />
April 2011<br />
|<br />
July 2022<br />
<br />
Twitter:<a href=""https://twitter.com/neuecc/"">@neuecc</a>
GitHub:<a href=""https://github.com/neuecc/"">neuecc</a>
</div>

<h3>Archive</h3>
<div class=""side_body"">
<ul>
{string.Join(Environment.NewLine, sideArchives)}
</ul>
</div>
";

// Create footer
var footer = @"<ul>
<li>Index: <a href=""https://neue.cc"">neue.cc</a><li>
<li>Powered by: <a href=""https://github.com/neuecc/Blog2"">https://github.com/neuecc/Blog2</a>
</ul>";

// Generate Root Index
var rootDir = outputDir;
CreateDirectory(rootDir, "");
await GenerateIndexWithPagingAsync(artciles.OrderByDescending(x => x), rootDir, null);

// Generate YYYY/index.html
await Parallel.ForEachAsync(artciles.GroupBy(x => x.Url.yyyy), async (yyyy, _) =>
{
    // item.OrderBy(x => x.OriginalFileName);
    var yyyyPath = CreateDirectory(rootDir, yyyy.Key);
    await GenerateIndexWithPagingAsync(yyyy.OrderByDescending(x => x), yyyyPath, $"{yyyy.Key}");

    // Generate mm/index.html
    foreach (var mm in yyyy.GroupBy(x => x.Url.mm))
    {
        var mmmmPath = CreateDirectory(yyyyPath, mm.Key);
        await GenerateIndexWithPagingAsync(mm.OrderByDescending(x => x), mmmmPath, $"{yyyy.Key}-{mm.Key}");

        // Generate single .html
        foreach (var item in mm)
        {
            var filePath = $"{item.Url.yyyy}/{item.Url.mm}/{item.Url.dd_no}.html";
            Console.WriteLine($"Generating {filePath}");
            var html = BuildHtml("neue cc - " + item.Title, item.Body, side, footer, $"https://neue.cc/{filePath}");
            await File.WriteAllTextAsync(Path.Combine(mmmmPath, item.Url.dd_no + ".html"), html);
        }
    }
});

/// <summary>Create and return combined path</summary>
string CreateDirectory(string root, string path)
{
    var dir = Path.Combine(root, path);
    if (!Directory.Exists(dir))
    {
        Directory.CreateDirectory(dir);
    }
    return dir;
}

(string FirstLine, string others) ReadAllTextWithoutFirstLine(string path)
{
    var lines = File.ReadLines(path);
    string? first = null;
    var others = new StringBuilder();
    foreach (var line in lines)
    {
        if (first == null)
        {
            first = line;
        }
        else
        {
            others.AppendLine(line);
        }
    }
    return (first!, others.ToString());
}

async Task GenerateIndexWithPagingAsync(IEnumerable<Article> source, string root, string? title)
{
    var pageRoot = root.Replace(outputDir, "").Trim('/');
    var urlRoot = pageRoot == "" ? "https://neue.cc" : ("https://neue.cc/" + pageRoot);
    var page = 1;
    var articles = source.Chunk(15).ToArray();
    foreach (var items in articles)
    {
        var body = new StringBuilder();
        foreach (var item in items)
        {
            body.AppendLine(item.Body);
        }

        // hasPrev
        if (page != 1)
        {
            if ((page - 1) == 1)
            {
                body.AppendLine($"<a href=\"{urlRoot}\">Prev |</a>");
            }
            else
            {
                body.AppendLine($"<a href=\"{urlRoot}/{page - 1}\">Prev |</a>");
            }
        }

        // hasNext
        if (page != articles.Length)
        {
            body.AppendLine($"<a href=\"{urlRoot}/{page + 1}\">| Next</a>");
        }

        var t = (title == null) ? "neue cc" : ("neue cc - " + title);
        var og = (title == null && page == 1) ? "https://neue.cc" : null;
        var html = BuildHtml(t, body.ToString(), side!, footer!, og);

        var path = (page == 1) ? "index.html" : $"{page}.html";
        var dir = root;
        Console.WriteLine("Generating " + title + " Page " + page);
        await File.WriteAllTextAsync(Path.Combine(dir, path), html);

        page++;
    }
}

public record Article(string Title, string Body, PageUrl Url) : IComparable<Article>
{
    public int CompareTo(Article? other)
    {
        if (other == null) return -1;
        return Comparer<(string, string, string)>.Default.Compare((Url.yyyy, Url.mm, Url.dd_no), (other.Url.yyyy, other.Url.mm, other.Url.dd_no));
    }
}
public record PageUrl(string yyyy, string mm, string dd_no);
public static class Extensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        return source.Where(x => x != null)!;
    }
}