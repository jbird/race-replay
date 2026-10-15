using System.CommandLine;

RootCommand root = new("Race Replay: build replay packages from OpenF1 sessions or seeded synthetic races.");

return await root.Parse(args).InvokeAsync();