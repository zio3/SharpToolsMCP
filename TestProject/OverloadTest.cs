using System;

namespace TestProject
{
    public class OverloadTest
    {
        public string Process(string input) 
        { 
            return $"String: {input}"; 
        }

        public string Process(int input) 
        { 
            return $"Int: {input}"; 
        }

        public string Process(double input) 
        { 
            return $"Double: {input}"; 
        }
    }
}