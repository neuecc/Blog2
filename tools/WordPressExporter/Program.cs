using System.Text.RegularExpressions;
using System.Xml.Linq;

// load wp_posts dump of neue.cc
var originalXml = XDocument.Load("wp_posts.xml");
var mapped = originalXml.Descendants("wp_posts")
    .Select(x => new wp_posts
    {
        ID = (ulong)x.Element("ID")!,
        post_auhtor = (long)x.Element("post_author")!,
        post_category = (long)x.Element("post_category")!,
        post_date = (DateTime)x.Element("post_date")!,
        post_date_gmt = (DateTime)x.Element("post_date_gmt")!,
        post_content = (string)x.Element("post_content")!,
        post_title = (string)x.Element("post_title")!,
        post_status = (string)x.Element("post_status")!,
        guid = (string)x.Element("guid")!,
        post_type = (string)x.Element("post_type")!,
    })
    .Where(x => x is { post_status: "publish", post_type: "post" } && x.guid.EndsWith(".html"))
    .OrderBy(x => x.post_date)
    .Select(x =>
    {
        // "http://neue.cc/2007/10/22_13.html"
        var match = Regex.Match(x.guid, @"(\d{4})/(\d{2})/(.+)\.html");
        var yyyy = match.Groups[1].Value;
        var mm = match.Groups[2].Value;
        var dd_no = match.Groups[3].Value;

        // <pre lang="..."></pre> -> ```... ```
        var body = x.post_content;
        body = body.Replace("</pre>", "```").Replace("<pre>", "```");
        body = Regex.Replace(body, "<pre lang=\"(.+)\">", "```$1");

        return new
        {
            Date = x.post_date,
            Url = new
            {
                yyyy,
                mm,
                dd_no,
            },
            Title = x.post_title,
            Body = body
        };
    })
    .ToArray();

// Output files

var dir = "articles";
if (!Directory.Exists(dir))
{
    Directory.CreateDirectory(dir);
}

Console.WriteLine("Output Files");
foreach (var item in mapped)
{
    var fileName = $"{item.Url.yyyy}-{item.Url.mm}-{item.Url.dd_no}.md";
    // # Title
    // body

    Console.WriteLine(fileName + " " + item.Title);
    File.WriteAllText(Path.Combine(dir, fileName), "# " + item.Title + Environment.NewLine + Environment.NewLine + item.Body);
}

#pragma warning disable CS8618 // allow nul to nullable

// table dump of WordPress posts db
public class wp_posts
{
    public ulong ID { get; set; }
    public long post_auhtor { get; set; }
    public long post_category { get; set; }
    public DateTime post_date { get; set; }
    public DateTime post_date_gmt { get; set; }
    public string post_content { get; set; }
    public string post_title { get; set; }
    public string post_status { get; set; } // enum('publish', 'draft', 'private', 'static', 'object', 'attachment', 'inherit', 'future', 'pending')
    public string guid { get; set; }
    public string post_type { get; set; }

    public override string ToString()
    {
        return post_date.ToString("yyyy/MM/dd") + " " + post_title;
    }
}

#pragma warning restore CS8618