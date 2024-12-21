// create a SK native plugin fro getting the current date and time
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Microsoft.SemanticKernel;

namespace localRAG.Plugins
{
    [Description("Get the current date and time")]
    public class DateTimePlugin 
    {
        [KernelFunction, Description("Get the current date and time")]
        public string GetDateTime()
        {
            return System.DateTime.Now.ToString();
        }
    }
}