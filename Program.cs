public class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var cli = new InteractiveRalen();
            if (args != null && args.Length > 0)
            {
                // treat all args as a single command line string and run once
                var single = string.Join(' ', args);
                return await cli.ExecuteSingleCommand(single);
            }
            else
            {
                await cli.RunREPL();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error: " + ex.Message);
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }
}
