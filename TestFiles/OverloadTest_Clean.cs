using System;

namespace TestNamespace {
    public class OverloadTest {
        /// <summary>
        /// Updated test method for overwriting
        /// </summary>
        /// <param name="input">Input parameter</param>
        /// <returns>Result string</returns>
        public string TestMethod(string input) {
            return $"Updated: {input}";
        }
        public void AnotherMethod() {
            Console.WriteLine("Another method");
        }
    }
}
