using System.Text.RegularExpressions;
namespace Together;
public static class OverlayText {
    public static string Clean(string? text,int limit) {
        if(string.IsNullOrWhiteSpace(text))return "";
        text=Regex.Replace(text,@"\{[^{}]*\}"," ");
        text=Regex.Replace(text,@"`[^`]*`|[A-Za-z0-9]+(?:[_:.\-/][A-Za-z0-9]+)+|\b[a-fA-F0-9]{16,}\b|\(O\)\d+", " ");
        text=Regex.Replace(text,@"[A-Za-z][A-Za-z0-9_]*(?:\s*=\s*[^，。；;\s]+)?", " ");
        text=Regex.Replace(text,@"[\[\]{}`]", " ");text=Regex.Replace(text,@"\s+"," ").Trim();
        if(!text.Any(c=>c>='\u4e00'&&c<='\u9fff'))return "";
        return text.Length>limit?text[..limit]+"…":text;
    }
}
