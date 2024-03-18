////////////////////////////////////////////////////////////////////////////////////////////////////////
//FileName: Program.cs
//Author : Ethan Hartman (ehh4525@rit.edu)
//Created On : 2/29/2024
//Last Modified On : 3/18/2024
//Description : Program to generate large prime numbers and the factors of odd numbers using the C# parallel libraries.
////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace System
{
    using System.Numerics;
    using System.Threading.Tasks;
    using System.Security.Cryptography;
    using System.Diagnostics;

    /// <summary>
    /// Class for a threadsafe object that can be incremented.
    /// </summary>
    public class ThreadsafeObject<T>
    {
        private static readonly string INVALID_TYPE_MESSAGE = "Unsupported type for operation.";
        private readonly object _lock = new();
        public T Value;

        public ThreadsafeObject(T value)
        {
            Value = value;
        }

        public static ThreadsafeObject<T> operator ++(ThreadsafeObject<T> obj)
        {
            try
            {
                Monitor.Enter(obj._lock);
                if (obj.Value is int l)
                    obj.Value = (T)(object)(l + 1);
                else if (obj.Value is BigInteger b)
                    obj.Value = (T)(object)(b + 1);
                else
                    throw new InvalidOperationException(INVALID_TYPE_MESSAGE);
                return obj;
            }
            finally { Monitor.Exit(obj._lock); }
        }

        public static ThreadsafeObject<T> operator +(ThreadsafeObject<T> obj, T value)
        {
            try
            {
                Monitor.Enter(obj._lock);
                if (obj.Value is BigInteger b && value is BigInteger b2)
                    obj.Value = (T)(object)(b + b2);
                else
                    throw new InvalidOperationException(INVALID_TYPE_MESSAGE);
                return obj;
            }
            finally { Monitor.Exit(obj._lock); }
        }
    }

    /// <summary>
    /// Extension methods for the BigInteger class providing prime checking, factor, and sqrt calculating.
    /// </summary>
    public static class BigIntegerPrimeExtensions
    {
        private static readonly BigInteger BIG_TWO = new(2);

        private static BigInteger RandomBigInteger(BigInteger minValue, BigInteger maxValue)
        {
            byte[] bytes = new byte[maxValue.ToByteArray().Length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return BigInteger.Remainder(new BigInteger(bytes), maxValue - minValue + 1) + minValue;
        }

        public static bool IsProbablyPrime(this BigInteger value, int k = 10)
        {
            if (value == 2 || value == 3)
                return true;
            else if (value <= 1 || value.IsEven)
                return false;

            BigInteger r = 0, d = value - 1;
            while (d % 2 == 0)
            {
                d /= 2;
                r++;
            }

            for (int i = 0; i < k; i++)
            {
                BigInteger rand = RandomBigInteger(BIG_TWO, value - 2);
                BigInteger x = BigInteger.ModPow(rand, d, value);

                if (x == 1 || x == value - 1)
                    continue;

                for(BigInteger j = 0; j < r - 1; j++)
                {
                    x = BigInteger.ModPow(x, BIG_TWO, value);

                    if (x == 1)
                        return false;

                    if (x == value - 1)
                        break;
                }

                if (x != value - 1)
                    return false;
            }
            
            return true;
        }

        public static BigInteger GetFactorCount(this BigInteger value)
        {
            BigInteger absVal = BigInteger.Abs(value);
            if (absVal == 1)
                return 1;
            else if (absVal == 0)
                throw new Exception("Cannot calculate factor count of 0.");

            BigInteger factorCount = new(2);
            for (BigInteger i = 3; i * i <= absVal; i += 2)
                if (absVal % i == 0)
                    factorCount += BIG_TWO; // i and number / i are factors, add both.

            return factorCount;
        }
    }

    /// <summary>
    /// Main class for the primechecker program.
    /// Allows users to input a bit count, and a method to generate prime or odd numbers.
    /// The user can also insert an optional count of numbers to generate, which defaults to 1.
    /// </summary>
    class Program
    {
        private static readonly string HELP_MESSAGE = "Usage: NumGen <bits> <option> <count>\n- bits - the number of bits of the number to be generated, this must be a multiple of 8, and at least 32 bits.\n- option - 'odd' or 'prime' (the type of numbers to be generated)\n- count - the count of numbers to generate, defaults to 1";
        private static readonly int BATCH_SIZE = 500;
        private static readonly string[] EMPTY_ARGS = Array.Empty<string>();
        private static readonly object printLock = new();
        private enum CheckMethod { Prime, Odd };

        private static (int, CheckMethod, int) ValidateArgs(string[] args)
        {
            int bitCount = -1, generations = 1;
            CheckMethod method = CheckMethod.Prime;
            string errorMessage = "";

            if (args.Length > 3 || args.Length < 2)
                errorMessage = HELP_MESSAGE;
            else
            {
                try
                {
                    bitCount = int.Parse(args[0]);
                    if (bitCount < 32 || bitCount % 8 != 0) // Bit count must be positive, and a multiple of 8 starting at 32
                        throw new Exception();
                } 
                catch (Exception) // If we have an exception, the bit count is invalid
                {
                    errorMessage += "Invalid bit count: Must be a positive Integer multiple of 8 in the range [32, 2^31-1]\n";
                }

                switch (args[1].ToLower())
                {
                    case "prime":
                        break;
                    case "odd":
                        method = CheckMethod.Odd;
                        break;
                    default:
                        errorMessage += "Invalid method: Must be either 'prime' or 'odd'\n";
                        break;
                }

                if (args.Length == 3) // If we have a third argument, it must be the valid generation count
                    try
                    {
                        generations = int.Parse(args[2]);
                        if (generations < 1)
                            throw new Exception();
                    }
                    catch (Exception) { errorMessage += "Invalid generations: Must be a positive Integer in the range [1, 2^31-1]\n"; }
            }

            if (errorMessage != "") // If we have an error message, print it and ask for new input recursively
            {
                Console.WriteLine(errorMessage);
                Console.Write("~>");
                string? input = Console.ReadLine();
                return ValidateArgs(input is not null ? input.Trim().Split(' ') : EMPTY_ARGS);
            }

            return (bitCount, method, generations);
        }

        private static bool ThreadsafePrintWithCount(ThreadsafeObject<int> counter, object toPrint, int maxPrint)
        {
            try
            {
                Monitor.Enter(printLock);
                if (counter.Value < maxPrint)
                {
                    if (counter.Value > 0) Console.WriteLine();
                    Console.WriteLine($"{counter++.Value}: {toPrint}");
                    if (counter.Value == maxPrint)
                        return true;
                }
                return false;
            }
            finally { Monitor.Exit(printLock); }
        }

        public static void Main(string[] args)
        {
            int bitCount, byteCount, generations;
            CheckMethod method;
            Stopwatch sw = new();

            for (;;)
            {
                ThreadsafeObject<int> printCounter = new(0);
                (bitCount, method, generations) = ValidateArgs(args);
                Console.WriteLine($"BitLength: {bitCount} bits");
                sw.Restart();

                byteCount = bitCount / 8;

                bool lastHasBeenPrinted = false;
                RandomNumberGenerator rng = RandomNumberGenerator.Create();
                while (!lastHasBeenPrinted) // Iterates a lot more than generations, but only prints the first calculated generations
                {
                    Task.Run(() => { // Utilizes thread pool for parallel generation
                        byte[] randomBytes = new byte[byteCount];
                        for (int i = 0; i < BATCH_SIZE && !lastHasBeenPrinted; i++)
                        {
                            rng.GetBytes(randomBytes);

                            BigInteger bigInt = BigInteger.Abs(new(randomBytes));
                            if (method == CheckMethod.Odd && !bigInt.IsEven && bigInt != 0)
                            {
                                if (ThreadsafePrintWithCount(printCounter, $"{bigInt}\nNumber of factors: {bigInt.GetFactorCount()}", generations))
                                    lastHasBeenPrinted = true;
                            }
                            else if (method == CheckMethod.Prime && bigInt.IsProbablyPrime())
                            {
                                if (ThreadsafePrintWithCount(printCounter, bigInt, generations))
                                    lastHasBeenPrinted = true;
                            }
                        }
                    });
                }

                while (!lastHasBeenPrinted) { }
                rng.Dispose();
                sw.Stop();
                args = EMPTY_ARGS;
                Console.WriteLine($"Time to Generate: {sw.Elapsed}");
                Console.Write("Press any key to continue...");
                Console.ReadKey();
                Console.WriteLine("\n");
            }
        }
    }
}