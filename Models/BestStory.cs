namespace HackerNewsAPIFacade.Models;

public record BestStory(string Title, string Uri, string PostedBy, DateTimeOffset Time, int Score, int CommentCount);