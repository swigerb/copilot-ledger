using System.Text;

namespace CopilotLedger.Cli;

// Builds a CreateProcess command line that the C runtime and CommandLineToArgvW split back into fileName and arguments.
// fileName is always quoted (Windows paths can't contain '"'). Each argument is written as is when it is non-empty and has
// no space, tab, newline, vertical tab, or '"'; otherwise it is quoted, a '"' becomes \", and backslashes are doubled
// when they come right before a '"' or the closing quote.
public static class WindowsCommandLine
{
    public static string Build(string fileName, IReadOnlyList<string> arguments)
    {
        StringBuilder builder = new();
        builder.Append('"').Append(fileName).Append('"');
        foreach (string argument in arguments)
        {
            builder.Append(' ');
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            {
                builder.Append(argument);
                continue;
            }

            builder.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', (backslashes * 2) + 1);
                }
                else
                {
                    builder.Append('\\', backslashes);
                }

                backslashes = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
        }

        return builder.ToString();
    }
}
