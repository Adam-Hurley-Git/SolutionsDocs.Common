using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace PowerDocu.Common
{
    /// <summary>
    /// Base class for HTML documentation builders. Provides helper methods
    /// for generating HTML content with a consistent template approach.
    /// The visual design is driven by an external CSS stylesheet, making
    /// it easy to customise the look-and-feel without touching the code.
    /// </summary>
    public abstract class HtmlBuilder
    {
        protected readonly Random random = new Random();

        // ------------------------------------------------------------------
        // Branding
        // ------------------------------------------------------------------

        /// <summary>
        /// Branding applied to every generated HTML page. Set once per documentation
        /// run via <see cref="ApplyBranding"/> before any HTML is generated; every
        /// <c>&lt;X&gt;HtmlBuilder</c> subclass picks it up automatically through the
        /// single shared <see cref="WrapInHtmlPage"/> choke point, so no subclass needs
        /// to know about branding itself. Defaults reproduce the stock PowerDocu look.
        /// </summary>
        private static string BrandAccentColor = "#0078d4";
        private static string BrandAccentColorDark = "#005a9e";
        private static string BrandSidebarColor = "#1e1e2e";
        private static string BrandName = "Solutions Docs";
        private static string BrandLogoDataUri = null;

        /// <summary>
        /// Applies branding config for the current documentation run. The logo (if any)
        /// is read once and embedded as a base64 data URI, so every generated page can
        /// reference it without needing a relative file path — output folders are nested
        /// at varying depths (solution root, component folders, per-action subfolders),
        /// which would otherwise make a single logo file awkward to link to consistently.
        /// </summary>
        public static void ApplyBranding(ConfigHelper config)
        {
            if (config == null) return;
            BrandAccentColor = string.IsNullOrEmpty(config.brandAccentColor) ? "#0078d4" : config.brandAccentColor;
            BrandAccentColorDark = string.IsNullOrEmpty(config.brandAccentColorDark) ? "#005a9e" : config.brandAccentColorDark;
            BrandSidebarColor = string.IsNullOrEmpty(config.brandSidebarColor) ? "#1e1e2e" : config.brandSidebarColor;
            BrandName = string.IsNullOrEmpty(config.brandName) ? "Solutions Docs" : config.brandName;
            BrandLogoDataUri = null;
            if (!string.IsNullOrEmpty(config.brandLogoPath) && File.Exists(config.brandLogoPath))
            {
                string mimeType = Path.GetExtension(config.brandLogoPath).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    ".webp" => "image/webp",
                    _ => null
                };
                if (mimeType != null)
                {
                    BrandLogoDataUri = $"data:{mimeType};base64,{ImageHelper.GetBase64(config.brandLogoPath)}";
                }
                else
                {
                    NotificationHelper.SendNotification($"Brand logo '{config.brandLogoPath}' has an unsupported extension; skipping logo.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Template helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the full HTML page wrapping the given body content.
        /// A &lt;link&gt; to <c>style.css</c> is included so that users can
        /// swap the stylesheet to change the design. Branding (colors/logo)
        /// is applied via inline CSS variable overrides and an optional
        /// embedded logo, driven by <see cref="ApplyBranding"/>.
        /// </summary>
        protected string WrapInHtmlPage(string title, string bodyContent, string navigationHtml, string cssRelativePath = "style.css")
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\">");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"  <title>{Encode(title)}</title>");
            sb.AppendLine($"  <link rel=\"stylesheet\" href=\"{cssRelativePath}\">");
            sb.AppendLine("  <style>");
            sb.AppendLine("    :root {");
            sb.AppendLine($"      --color-primary: {BrandAccentColor};");
            sb.AppendLine($"      --color-primary-dark: {BrandAccentColorDark};");
            sb.AppendLine($"      --color-sidebar: {BrandSidebarColor};");
            sb.AppendLine("    }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"page-wrapper\">");
            sb.AppendLine("  <nav class=\"sidebar\">");
            if (!string.IsNullOrEmpty(BrandLogoDataUri) || !string.IsNullOrEmpty(BrandName))
            {
                sb.AppendLine("    <div class=\"brand-header\">");
                if (!string.IsNullOrEmpty(BrandLogoDataUri))
                {
                    sb.AppendLine($"      <img class=\"brand-logo-img\" src=\"{BrandLogoDataUri}\" alt=\"{Encode(BrandName)}\" />");
                }
                if (!string.IsNullOrEmpty(BrandName))
                {
                    sb.AppendLine($"      <div class=\"brand-title\">{Encode(BrandName)}</div>");
                }
                sb.AppendLine("    </div>");
            }
            sb.AppendLine(navigationHtml);
            sb.AppendLine("  </nav>");
            sb.AppendLine("  <main class=\"content\">");
            sb.AppendLine(bodyContent);
            sb.AppendLine("  </main>");
            sb.AppendLine("</div>");
            // Collapsible navigation toggle script
            sb.AppendLine("<script>");
            sb.AppendLine("document.querySelectorAll('.nav-toggle').forEach(function(btn){");
            sb.AppendLine("  btn.addEventListener('click',function(e){");
            sb.AppendLine("    e.preventDefault();");
            sb.AppendLine("    var parent=this.closest('.nav-parent');");
            sb.AppendLine("    parent.classList.toggle('collapsed');");
            sb.AppendLine("  });");
            sb.AppendLine("});");
            sb.AppendLine("</script>");
            // Active-page highlighting: compares each nav link's target file/anchor against
            // the current page, so the sidebar shows which page/section is currently open.
            // Client-side because the same nav HTML is reused as-is across every page linking
            // to it, and per-page files can be nested at varying folder depths.
            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");
            sb.AppendLine("  function currentFile(){ return window.location.pathname.split('/').pop() || 'index.html'; }");
            sb.AppendLine("  function updateActiveNav(){");
            sb.AppendLine("    var file = currentFile();");
            sb.AppendLine("    var hash = window.location.hash;");
            sb.AppendLine("    document.querySelectorAll('.nav-list a').forEach(function(a){");
            sb.AppendLine("      var href = a.getAttribute('href') || '';");
            sb.AppendLine("      var hashIdx = href.indexOf('#');");
            sb.AppendLine("      var hrefFile = hashIdx >= 0 ? href.substring(0, hashIdx) : href;");
            sb.AppendLine("      var hrefHash = hashIdx >= 0 ? href.substring(hashIdx) : '';");
            sb.AppendLine("      if (hrefFile === '') hrefFile = file;");
            sb.AppendLine("      var isActive = (hrefFile === file) && (hrefHash === hash);");
            sb.AppendLine("      a.classList.toggle('active', isActive);");
            sb.AppendLine("      if (isActive){");
            sb.AppendLine("        var parent = a.closest('.nav-parent');");
            sb.AppendLine("        while (parent){ parent.classList.remove('collapsed'); parent = parent.parentElement ? parent.parentElement.closest('.nav-parent') : null; }");
            sb.AppendLine("      }");
            sb.AppendLine("    });");
            sb.AppendLine("  }");
            sb.AppendLine("  updateActiveNav();");
            sb.AppendLine("  window.addEventListener('hashchange', updateActiveNav);");
            sb.AppendLine("})();");
            sb.AppendLine("</script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Element helpers
        // ------------------------------------------------------------------

        protected static string Encode(string text)
        {
            return HttpUtility.HtmlEncode(text ?? "");
        }

        protected static string Heading(int level, string text)
        {
            return $"<h{level}>{Encode(text)}</h{level}>";
        }

        protected static string HeadingWithId(int level, string text, string id)
        {
            return $"<h{level} id=\"{Encode(id)}\">{Encode(text)}</h{level}>";
        }

        protected static string HeadingRaw(int level, string innerHtml)
        {
            return $"<h{level}>{innerHtml}</h{level}>";
        }

        protected static string Paragraph(string text)
        {
            return $"<p>{Encode(text)}</p>";
        }

        protected static string ParagraphRaw(string innerHtml)
        {
            return $"<p>{innerHtml}</p>";
        }

        protected static string ParagraphWithLinebreaks(string text)
        {
            return ParagraphRaw(Encode(text).Replace("\r\n", "<br/>").Replace("\n", "<br/>"));
        }

        protected static string Link(string text, string href)
        {
            return $"<a href=\"{Encode(href)}\">{Encode(text)}</a>";
        }

        /// <summary>
        /// Creates a URL-safe anchor ID from a control/element name.
        /// Lowercases, replaces spaces with hyphens, and removes unsafe characters.
        /// </summary>
        protected static string SanitizeAnchorId(string name)
        {
            if (String.IsNullOrEmpty(name)) return "";
            return System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant().Replace(" ", "-"), "[^a-z0-9_-]", "");
        }

        protected static string Image(string alt, string src)
        {
            return $"<img src=\"{Encode(src)}\" alt=\"{Encode(alt)}\" />";
        }

        protected static string ImageWithClass(string alt, string src, string cssClass)
        {
            return $"<img src=\"{Encode(src)}\" alt=\"{Encode(alt)}\" class=\"{cssClass}\" />";
        }

        protected static string CodeBlock(string code)
        {
            if (String.IsNullOrEmpty(code)) return "";
            return $"<code>{Encode(code)}</code>";
        }

        protected static string PreCodeBlock(string code)
        {
            if (String.IsNullOrEmpty(code)) return "";
            return $"<pre><code>{Encode(code)}</code></pre>";
        }

        // ------------------------------------------------------------------
        // Table helpers
        // ------------------------------------------------------------------

        protected static string TableStart(params string[] headers)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr>");
            foreach (string h in headers)
            {
                sb.AppendLine($"  <th>{Encode(h)}</th>");
            }
            sb.AppendLine("</tr></thead>");
            sb.AppendLine("<tbody>");
            return sb.ToString();
        }

        protected static string TableRow(params string[] cells)
        {
            StringBuilder sb = new StringBuilder("<tr>");
            foreach (string c in cells)
            {
                sb.Append($"<td>{Encode(c)}</td>");
            }
            sb.Append("</tr>");
            return sb.ToString();
        }

        /// <summary>
        /// Table row allowing raw HTML in cells (caller is responsible for encoding).
        /// </summary>
        protected static string TableRowRaw(params string[] cells)
        {
            StringBuilder sb = new StringBuilder("<tr>");
            foreach (string c in cells)
            {
                sb.Append($"<td>{c}</td>");
            }
            sb.Append("</tr>");
            return sb.ToString();
        }

        protected static string TableEnd()
        {
            return "</tbody></table>";
        }

        // ------------------------------------------------------------------
        // Navigation helpers
        // ------------------------------------------------------------------

        protected static string NavigationList(IEnumerable<(string label, string href)> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<ul class=\"nav-list\">");
            foreach (var item in items)
            {
                bool isTech = IsTechNavItem(item.label, out string techIcon);
                string liClass = isTech ? " class=\"nav-tech\"" : "";
                sb.AppendLine($"  <li{liClass}><a href=\"{Encode(item.href)}\">{RenderNavLinkInner(item.label, isTech, techIcon)}</a></li>");
            }
            sb.AppendLine("</ul>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // "Technology" nav items — the main building blocks of a solution
        // (Flows, Apps, Agents, Tables, and reserved for a future SharePoint
        // section), shown bigger with an icon in the sidebar.
        //
        // Icon sourcing, in priority order:
        //  1. A real connector icon PowerDocu already downloads/caches via
        //     ConnectorHelper (e.g. the actual Dataverse/SharePoint icons used
        //     elsewhere for connector references) — used when available.
        //  2. An icon PowerDocu already ships inline elsewhere in the codebase
        //     (AgentIcon.cs — used for Copilot Studio topic diagrams), reused
        //     here for visual consistency rather than inventing a new one.
        //  3. A small generic placeholder glyph as a last resort, for
        //     Flows/Apps/Tables/SharePoint labels neither of the above
        //     currently cover (there's no official "Power Apps" icon anywhere
        //     in this codebase, and connector icons are only present once
        //     downloaded via `PowerDocu.exe -i` at least once).
        // ------------------------------------------------------------------

        // Real connector icons ConnectorHelper already knows how to fetch/cache —
        // reuse them instead of drawing new ones.
        private static readonly Dictionary<string, string> TechConnectorIconNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Tables"] = "commondataservice", // Microsoft Dataverse
            ["SharePoint"] = "sharepointonline",
            ["Apps"] = "powertools", // real Power Apps logo, cached under the "Microsoft Power Apps" connector entry
        };

        // Real Microsoft product logos shipped as a Resources file (not fetched via
        // ConnectorHelper, since these aren't "connectors") — path resolved the same
        // way ConfigHelper resolves brandLogoPath.
        private static readonly Dictionary<string, string> TechResourceIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Flows"] = AssemblyHelper.GetExecutablePath() + @"\Resources\PowerAutomateLogo.png",
        };

        // Icons PowerDocu already draws inline elsewhere (AgentIcon.cs), reused as-is.
        private static readonly Dictionary<string, string> TechReusedIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Agents"] = AddViewBoxIfMissing(AgentIcon.MessageIcon, "0 0 20 20"),
        };

        // Last-resort generic glyphs, used only when neither of the above apply.
        private static readonly Dictionary<string, string> TechFallbackIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Apps"] = "<svg viewBox=\"0 0 24 24\"><rect x=\"3\" y=\"3\" width=\"8\" height=\"8\" rx=\"1.5\"/><rect x=\"13\" y=\"3\" width=\"8\" height=\"8\" rx=\"1.5\"/><rect x=\"3\" y=\"13\" width=\"8\" height=\"8\" rx=\"1.5\"/><rect x=\"13\" y=\"13\" width=\"8\" height=\"8\" rx=\"1.5\"/></svg>",
            ["Tables"] = "<svg viewBox=\"0 0 24 24\"><path d=\"M12 3c-5 0-9 1.34-9 3v12c0 1.66 4 3 9 3s9-1.34 9-3V6c0-1.66-4-3-9-3zm0 2c4.42 0 7 1.06 7 1.5S16.42 8 12 8 5 6.94 5 6.5 7.58 5 12 5zm-7 3.65c1.53.85 4.16 1.35 7 1.35s5.47-.5 7-1.35v3.02c-1.53.85-4.16 1.35-7 1.35s-5.47-.5-7-1.35V8.65zm0 5.02c1.53.85 4.16 1.35 7 1.35s5.47-.5 7-1.35v3.02c-1.53.85-4.16 1.35-7 1.35s-5.47-.5-7-1.35v-3.02z\"/></svg>",
            ["SharePoint"] = "<svg viewBox=\"0 0 24 24\"><path d=\"M3 5a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5z\"/></svg>",
            // Only reached if PowerAutomateLogo.png is somehow missing from Resources\ (shouldn't happen on a normal build/publish).
            ["Flows"] = AddViewBoxIfMissing(AgentIcon.FlowIcon, "0 0 20 20"),
        };

        /// <summary>
        /// Some existing icon sources (e.g. <see cref="AgentIcon"/>) set only width/height
        /// on their &lt;svg&gt;, no viewBox — fine as-is, but breaks when this nav rendering
        /// later resizes the icon via CSS (content clips instead of scaling). Injects a
        /// matching viewBox without altering anything else about the source icon.
        /// </summary>
        private static string AddViewBoxIfMissing(string svg, string viewBox)
        {
            if (string.IsNullOrEmpty(svg) || svg.Contains("viewBox")) return svg;
            int gt = svg.IndexOf('>');
            return gt < 0 ? svg : svg.Insert(gt, $" viewBox=\"{viewBox}\"");
        }

        private static string GetConnectorIconDataUri(string connectorUniqueName)
        {
            string path = ConnectorHelper.getConnectorIconFile(connectorUniqueName);
            return GetFileIconDataUri(path);
        }

        private static string GetFileIconDataUri(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string mimeType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                _ => "image/png"
            };
            return $"data:{mimeType};base64,{ImageHelper.GetBase64(path)}";
        }

        private static bool IsTechNavItem(string label, out string iconMarkup)
        {
            iconMarkup = null;
            if (label == null) return false;

            if (TechConnectorIconNames.TryGetValue(label, out string connectorName))
            {
                string dataUri = GetConnectorIconDataUri(connectorName);
                if (dataUri != null)
                {
                    iconMarkup = $"<img src=\"{dataUri}\" alt=\"\" />";
                    return true;
                }
            }
            if (TechResourceIcons.TryGetValue(label, out string resourcePath))
            {
                string dataUri = GetFileIconDataUri(resourcePath);
                if (dataUri != null)
                {
                    iconMarkup = $"<img src=\"{dataUri}\" alt=\"\" />";
                    return true;
                }
            }
            if (TechReusedIcons.TryGetValue(label, out iconMarkup)) return true;
            if (TechFallbackIcons.TryGetValue(label, out iconMarkup)) return true;
            return false;
        }

        private static string RenderNavLinkInner(string label, bool isTech, string iconMarkup)
        {
            if (!isTech) return Encode(label);
            return $"<span class=\"nav-tech-icon\">{iconMarkup}</span><span class=\"nav-tech-label\">{Encode(label)}</span>";
        }

        /// <summary>
        /// Renders a hierarchical navigation list with collapsible parent items.
        /// Items with children get a toggle arrow; their children are nested in a sub-list.
        /// </summary>
        protected static string NavigationList(IEnumerable<(string label, string href, int level)> items)
        {
            var list = items.ToList();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<ul class=\"nav-list\">");
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                // Check if this item has children (next item is a higher level)
                bool hasChildren = (i + 1 < list.Count) && (list[i + 1].level > item.level);
                bool itemIsTech = IsTechNavItem(item.label, out string itemTechIcon);
                if (hasChildren)
                {
                    sb.AppendLine($"  <li class=\"nav-parent collapsed{(itemIsTech ? " nav-tech" : "")}\">");
                    sb.AppendLine($"    <div class=\"nav-parent-row\"><a href=\"{Encode(item.href)}\">{RenderNavLinkInner(item.label, itemIsTech, itemTechIcon)}</a><button class=\"nav-toggle\" aria-label=\"Toggle\">&#9662;</button></div>");
                    int childLevel = list[i + 1].level;
                    sb.AppendLine($"    <ul class=\"nav-children\">");
                    i++;
                    while (i < list.Count && list[i].level >= childLevel)
                    {
                        var child = list[i];
                        // Check if this child also has children
                        bool childHasChildren = (i + 1 < list.Count) && (list[i + 1].level > child.level);
                        bool childIsTech = IsTechNavItem(child.label, out string childTechIcon);
                        if (childHasChildren && child.level == childLevel)
                        {
                            sb.AppendLine($"      <li class=\"nav-parent collapsed{(childIsTech ? " nav-tech" : "")}\">");
                            sb.AppendLine($"        <div class=\"nav-parent-row\"><a href=\"{Encode(child.href)}\">{RenderNavLinkInner(child.label, childIsTech, childTechIcon)}</a><button class=\"nav-toggle\" aria-label=\"Toggle\">&#9662;</button></div>");
                            int grandchildLevel = list[i + 1].level;
                            sb.AppendLine($"        <ul class=\"nav-children\">");
                            i++;
                            while (i < list.Count && list[i].level >= grandchildLevel)
                            {
                                sb.AppendLine($"          <li class=\"nav-sub-{list[i].level}\"><a href=\"{Encode(list[i].href)}\">{Encode(list[i].label)}</a></li>");
                                i++;
                            }
                            sb.AppendLine($"        </ul>");
                            sb.AppendLine($"      </li>");
                            i--; // compensate for outer loop increment
                        }
                        else
                        {
                            sb.AppendLine($"      <li class=\"nav-sub-{child.level}{(childIsTech ? " nav-tech" : "")}\"><a href=\"{Encode(child.href)}\">{RenderNavLinkInner(child.label, childIsTech, childTechIcon)}</a></li>");
                        }
                        i++;
                    }
                    i--; // compensate for outer for-loop increment
                    sb.AppendLine($"    </ul>");
                    sb.AppendLine($"  </li>");
                }
                else
                {
                    sb.AppendLine($"  <li{(itemIsTech ? " class=\"nav-tech\"" : "")}><a href=\"{Encode(item.href)}\">{RenderNavLinkInner(item.label, itemIsTech, itemTechIcon)}</a></li>");
                }
            }
            sb.AppendLine("</ul>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Bullet list helpers
        // ------------------------------------------------------------------

        protected static string BulletListStart() => "<ul>";
        protected static string BulletListEnd() => "</ul>";
        protected static string BulletItem(string text) => $"<li>{Encode(text)}</li>";
        protected static string BulletItemRaw(string innerHtml) => $"<li>{innerHtml}</li>";

        // ------------------------------------------------------------------
        // Expression helpers (mirrors MarkdownBuilder helpers)
        // ------------------------------------------------------------------

        protected string AddExpressionDetails(List<Expression> inputs)
        {
            StringBuilder tableSB = new StringBuilder("<table class=\"expression-table\">");
            foreach (Expression input in inputs)
            {
                StringBuilder operandsCellSB = new StringBuilder("<td>");

                if (input.expressionOperands.Count > 1)
                {
                    StringBuilder operandsTableSB = new StringBuilder("<table class=\"expression-table\">");
                    foreach (object actionInputOperand in input.expressionOperands)
                    {
                        if (actionInputOperand.GetType() == typeof(Expression))
                        {
                            operandsTableSB.Append(AddExpressionTable((Expression)actionInputOperand, false));
                        }
                        else
                        {
                            operandsTableSB.Append("<tr><td>").Append(Encode(actionInputOperand.ToString())).Append("</td></tr>");
                        }
                    }
                    operandsTableSB.Append("</table>");
                    operandsCellSB.Append(operandsTableSB).Append("</td>");
                }
                else
                {
                    if (input.expressionOperands.Count > 0)
                    {
                        if (input.expressionOperands[0]?.GetType() == typeof(Expression))
                        {
                            operandsCellSB.Append(AddExpressionTable((Expression)input.expressionOperands[0]).Append("</table>"));
                        }
                        else if (input.expressionOperands[0]?.GetType() == typeof(string))
                        {
                            operandsCellSB.Append(Encode(input.expressionOperands[0]?.ToString()));
                        }
                        else if (input.expressionOperands[0]?.GetType() == typeof(List<object>))
                        {
                            operandsCellSB.Append("<table class=\"expression-table\">");
                            foreach (object obj in (List<object>)input.expressionOperands[0])
                            {
                                if (obj.GetType().Equals(typeof(Expression)))
                                {
                                    operandsCellSB.Append(AddExpressionTable((Expression)obj, false));
                                }
                                else if (obj.GetType().Equals(typeof(List<object>)))
                                {
                                    foreach (object o in (List<object>)obj)
                                    {
                                        operandsCellSB.Append(AddExpressionTable((Expression)o, false));
                                    }
                                }
                            }
                            operandsCellSB.Append("</table>");
                        }
                    }
                    else
                    {
                        operandsCellSB.Append("");
                    }
                    operandsCellSB.Append("</td>");
                }
                tableSB.Append("<tr><td>").Append(Encode(input.expressionOperator)).Append("</td>").Append(operandsCellSB).Append("</tr>");
            }
            tableSB.Append("</table>");
            return tableSB.ToString();
        }

        protected StringBuilder AddExpressionTable(Expression expression, bool createNewTable = true, bool firstColumnBold = false)
        {
            StringBuilder table = createNewTable ? new StringBuilder("<table class=\"expression-table\">") : new StringBuilder();

            if (expression?.expressionOperator != null)
            {
                StringBuilder tr = new StringBuilder("<tr>");
                StringBuilder tc = new StringBuilder("<td>");

                if (firstColumnBold)
                {
                    tc.Append("<b>").Append(Encode(expression.expressionOperator)).Append("</b>");
                }
                else
                {
                    tc.Append(Encode(expression.expressionOperator));
                }
                tr.Append(tc.Append("</td>"));
                tc = new StringBuilder("<td>");
                if (expression.expressionOperands.Count > 1)
                {
                    StringBuilder operandsTable = new StringBuilder("<table class=\"expression-table\">");
                    foreach (var expressionOperand in expression.expressionOperands.OrderBy(o => o.ToString()).ToList())
                    {
                        if (expressionOperand.GetType().Equals(typeof(string)))
                        {
                            operandsTable.Append("<tr><td>").Append(CodeBlock((string)expressionOperand)).Append("</td></tr>");
                        }
                        else if (expressionOperand.GetType().Equals(typeof(Expression)))
                        {
                            operandsTable.Append(AddExpressionTable((Expression)expressionOperand, false));
                        }
                        else
                        {
                            operandsTable.Append("<tr><td></td></tr>");
                        }
                    }
                    tc.Append(operandsTable).Append("</table>");
                }
                else if (expression.expressionOperands.Count == 0)
                {
                    // nothing to do
                }
                else
                {
                    object expo = expression.expressionOperands[0];
                    if (expo.GetType().Equals(typeof(string)))
                    {
                        tc.Append((expression.expressionOperands.Count == 0) ? "" : CodeBlock(expression.expressionOperands[0]?.ToString()));
                    }
                    else if (expo.GetType().Equals(typeof(List<object>)))
                    {
                        foreach (object obj in (List<object>)expo)
                        {
                            if (obj.GetType().Equals(typeof(List<object>)))
                            {
                                foreach (object o in (List<object>)obj)
                                {
                                    tc.Append(AddExpressionTable((Expression)o, true));
                                }
                            }
                            else if (obj.GetType().Equals(typeof(Expression)))
                            {
                                tc.Append(AddExpressionTable((Expression)obj, true));
                            }
                            else
                            {
                                tc.Append(Encode(obj.ToString())).Append("<br/>");
                            }
                        }
                    }
                    else if (expo.GetType().Equals(typeof(Expression)))
                    {
                        tc.Append(AddExpressionTable((Expression)expo, true));
                    }
                }
                tr.Append(tc).Append("</td>");
                table.Append(tr.Append("</tr>"));
            }
            if (createNewTable)
            {
                table.Append("</table>");
            }
            return table;
        }

        // ------------------------------------------------------------------
        // File helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes the default CSS stylesheet to the target folder if it does
        /// not already exist. This allows it to be replaced with a custom one.
        /// </summary>
        protected static void WriteDefaultStylesheet(string folderPath)
        {
            string cssPath = Path.Combine(folderPath, "style.css");
            File.WriteAllText(cssPath, GetDefaultCss());
        }

        protected void SaveHtmlFile(string filePath, string htmlContent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, htmlContent, Encoding.UTF8);
        }

        // ------------------------------------------------------------------
        // Default CSS
        // ------------------------------------------------------------------

        public static string GetDefaultCss()
        {
            return @"/* Solutions Docs HTML Documentation Stylesheet
   Replace this file to change the visual appearance of the generated documentation. */

:root {
    --color-primary: #0078d4;
    --color-primary-dark: #005a9e;
    --color-bg: #ffffff;
    --color-bg-alt: #f5f6fa;
    --color-sidebar: #1e1e2e;
    --color-sidebar-text: #cdd6f4;
    --color-sidebar-hover: #313244;
    --color-text: #1e1e2e;
    --color-text-light: #585b70;
    --color-border: #e0e0e0;
    --color-success: #ccffcc;
    --color-danger: #ffcccc;
    --radius: 8px;
    --shadow: 0 1px 3px rgba(0,0,0,0.08);
    --font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
    --font-mono: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
}

*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
    font-family: var(--font-family);
    font-size: 15px;
    line-height: 1.6;
    color: var(--color-text);
    background: var(--color-bg);
}

/* Page layout */
.page-wrapper {
    display: flex;
    min-height: 100vh;
}

/* Sidebar navigation */
.sidebar {
    width: 260px;
    min-width: 260px;
    background: var(--color-sidebar);
    color: var(--color-sidebar-text);
    padding: 1.5rem 0;
    position: sticky;
    top: 0;
    height: 100vh;
    overflow-y: auto;
}

.brand-header {
    padding: 1rem 1.25rem 0.85rem;
    text-align: left;
    border-bottom: 1px solid var(--color-sidebar-hover);
    margin-bottom: 0.75rem;
}

.brand-logo-img {
    display: block;
    max-width: 70%;
    max-height: 24px;
    border-radius: 0;
    margin-bottom: 0.5rem;
}

.brand-title {
    font-size: 0.95rem;
    font-weight: 700;
    letter-spacing: 0.02em;
    color: #fff;
    text-transform: uppercase;
}

/* Solution/component name — always show the full name, never clip it */
.sidebar .nav-title {
    font-size: 1.1rem;
    font-weight: 600;
    padding: 0 1.25rem 1rem;
    color: #fff;
    border-bottom: 1px solid var(--color-sidebar-hover);
    margin-bottom: 0.5rem;
    overflow-wrap: break-word;
    word-break: break-word;
    white-space: normal;
    line-height: 1.3;
}

/* Technology nav sections (Flows, Apps, SharePoint, ...) — the main building
   blocks of a solution, visually promoted above secondary metadata sections. */
/* !important: Flows/Apps/SharePoint can render at any nesting depth (top-level or
   nested inside the Solution Components group), and deeper nav-sub/nested-parent-row
   rules elsewhere in this stylesheet have higher selector specificity. This class
   should always win regardless of where it ends up in the tree. */
.nav-tech > .nav-parent-row > a,
.nav-tech > a {
    font-size: 1rem !important;
    font-weight: 700 !important;
    padding-left: 1.25rem !important;
}

.nav-tech-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 18px;
    height: 18px;
    margin-right: 0.6rem;
    flex-shrink: 0;
    vertical-align: middle;
}

.nav-tech-icon svg {
    width: 100%;
    height: 100%;
    fill: currentColor;
}

.nav-tech-icon img {
    width: 100%;
    height: 100%;
    object-fit: contain;
    border-radius: 0;
}

.nav-list {
    list-style: none;
    padding: 0;
}

.nav-list li a {
    display: block;
    padding: 0.5rem 1.25rem;
    color: var(--color-sidebar-text);
    text-decoration: none;
    font-size: 0.9rem;
    transition: background 0.15s, color 0.15s;
    border-left: 3px solid transparent;
}

.nav-list li a:hover,
.nav-list li a.active {
    background: var(--color-sidebar-hover);
    color: #fff;
    border-left-color: var(--color-primary);
}

/* Collapsible parent items */
.nav-parent-row {
    display: flex;
    align-items: center;
}

.nav-parent-row a {
    flex: 1;
    display: block;
    padding: 0.5rem 1.25rem;
    color: var(--color-sidebar-text);
    text-decoration: none;
    font-size: 0.9rem;
    transition: background 0.15s, color 0.15s;
    border-left: 3px solid transparent;
}

.nav-parent-row a:hover {
    background: var(--color-sidebar-hover);
    color: #fff;
    border-left-color: var(--color-primary);
}

.nav-toggle {
    background: none;
    border: none;
    color: var(--color-sidebar-text);
    cursor: pointer;
    padding: 0.5rem 0.75rem;
    font-size: 0.7rem;
    transition: transform 0.2s;
    flex-shrink: 0;
}

.nav-toggle:hover {
    color: #fff;
}

.nav-parent.collapsed .nav-toggle {
    transform: rotate(-90deg);
}

.nav-children {
    list-style: none;
    padding: 0;
    overflow: hidden;
}

.nav-parent.collapsed > .nav-children {
    display: none;
}

/* Sub-level indentation */
.nav-list li.nav-sub-1 > a,
.nav-children > li.nav-sub-1 > a {
    padding-left: 2.25rem;
    font-size: 0.84rem;
}

.nav-list li.nav-sub-2 > a,
.nav-children > li.nav-sub-2 > a {
    padding-left: 3.25rem;
    font-size: 0.8rem;
}

/* Nested parent rows at sub-level */
.nav-children > li.nav-parent > .nav-parent-row > a {
    padding-left: 2.25rem;
    font-size: 0.84rem;
}

/* Main content area */
.content {
    flex: 1;
    padding: 2rem 3rem;
    max-width: 1100px;
}

h1 {
    font-size: 1.75rem;
    font-weight: 700;
    color: var(--color-primary-dark);
    margin-bottom: 1rem;
    padding-bottom: 0.5rem;
    border-bottom: 2px solid var(--color-primary);
}

h2 {
    font-size: 1.35rem;
    font-weight: 600;
    color: var(--color-text);
    margin: 1.75rem 0 0.75rem;
    padding-bottom: 0.35rem;
    border-bottom: 1px solid var(--color-border);
}

h3 {
    font-size: 1.1rem;
    font-weight: 600;
    margin: 1.25rem 0 0.5rem;
}

h4 {
    font-size: 1rem;
    font-weight: 600;
    margin: 1rem 0 0.5rem;
}

p { margin-bottom: 0.75rem; }

a { color: var(--color-primary); text-decoration: none; }
a:hover { text-decoration: underline; }

/* Tables */
table {
    width: 100%;
    border-collapse: collapse;
    margin: 0.75rem 0 1.25rem;
    background: var(--color-bg);
    border-radius: var(--radius);
    overflow: hidden;
    box-shadow: var(--shadow);
}

thead {
    background: var(--color-primary);
    color: #fff;
}

th {
    padding: 0.6rem 0.85rem;
    text-align: left;
    font-weight: 600;
    font-size: 0.85rem;
    text-transform: uppercase;
    letter-spacing: 0.03em;
}

td {
    padding: 0.55rem 0.85rem;
    border-bottom: 1px solid var(--color-border);
    vertical-align: top;
    font-size: 0.9rem;
}

tbody tr:nth-child(even) { background: var(--color-bg-alt); }
tbody tr:hover { background: #eef2ff; }

/* Nested expression tables */
table.expression-table {
    box-shadow: none;
    margin: 0.25rem 0;
    font-size: 0.85rem;
}

table.expression-table thead { background: var(--color-text-light); }

/* Code blocks */
code {
    font-family: var(--font-mono);
    font-size: 0.85em;
    background: var(--color-bg-alt);
    padding: 0.15em 0.4em;
    border-radius: 4px;
    border: 1px solid var(--color-border);
}

/* Images */
img {
    max-width: 100%;
    height: auto;
    border-radius: var(--radius);
}

img.icon-inline {
    width: 16px;
    height: 16px;
    vertical-align: middle;
    border-radius: 0;
}

/* Lists */
ul, ol {
    padding-left: 1.5rem;
    margin-bottom: 0.75rem;
}

li { margin-bottom: 0.25rem; }

/* Color preview swatch */
.color-swatch {
    display: inline-block;
    width: 20px;
    height: 20px;
    border: 1px solid var(--color-border);
    border-radius: 4px;
    vertical-align: middle;
    margin-right: 0.35rem;
}

/* Changed defaults highlight */
.changed-value { background-color: var(--color-success); padding: 0.2rem 0.4rem; border-radius: 4px; }
.default-value { background-color: var(--color-danger); padding: 0.2rem 0.4rem; border-radius: 4px; }

/* Responsive */
@media (max-width: 768px) {
    .page-wrapper { flex-direction: column; }
    .sidebar {
        width: 100%;
        min-width: 100%;
        height: auto;
        position: relative;
    }
    .content { padding: 1rem; }
}
";
        }
    }
}
