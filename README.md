# Hacker News API Facade

## How to run

1. Clone the git repo from: https://github.com/fabiogaluppo/HackerNewsAPIFacade
2. Restore dotnet project: dotnet restore
3. Run dotnet project: dotnet run -c Release --urls "http://localhost:8080"

After running, use curl and jq to perform integration tests. For example:
* curl "http://localhost:8080/api/beststories?n=10"
* curl "http://localhost:8080/api/beststories?n=0"
* curl "http://localhost:8080/api/beststories?n=1000"
* curl -s "http://localhost:8080/api/beststories?n=30" | jq "map(.score) | . == (sort | reverse)"

Refer to the images directory for supporting evidence of the test executions and the application runtime. 

## The implementation

The solution is implemented as a Minimal ASP.NET Core 8 API and includes robust capabilities such as rate limiting, response caching, retry mechanism, and structured error handling to ensure resilience and protect both the Hacker News API and the facade service.

## Future enhancements

While the implementation aims to be concise, it could benefit from improved organization, particularly by isolating infrastructure-related concerns, such as the retry policy, into dedicated components.

There are several additional improvements that could be considered in the future. The following list is not exhaustive but includes:
* Add unit tests (including mocked dependencies)
* Profile the running application to identify performance bottlenecks
* Configure additional ASP.NET Core services (e.g., Authentication, Authorization, Kestrel, etc.)
* Deploy the application in a containerized environment
* Improve logging for better observability and diagnostics
* Perform load testing to verify the effectiveness of caching and throttling mechanisms
* Move hardcoded default values in the code (e.g., TTL, maximum number of concurrent calls) to the configuration file
