namespace AgenticSoftwareEngineering.Api.Application.Urls;

public interface IShortCodeGenerator
{
    string Generate();
}

public sealed class SecureShortCodeGenerator : IShortCodeGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int CodeLength = 7;
    private const int RejectionLimit = 248;

    public string Generate()
    {
        Span<char> code = stackalloc char[CodeLength];
        Span<byte> randomBytes = stackalloc byte[CodeLength];
        var index = 0;
        while (index < code.Length)
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
            foreach (var randomByte in randomBytes)
            {
                if (randomByte >= RejectionLimit)
                {
                    continue;
                }

                code[index++] = Alphabet[randomByte % Alphabet.Length];
                if (index == code.Length)
                {
                    break;
                }
            }
        }

        return new string(code);
    }
}