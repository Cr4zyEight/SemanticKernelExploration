using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Memory;
using SemanticKernelExploration.Models;

namespace SemanticKernelExploration.Plugins
{
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0050 
    public class MemoryPlugin
    {
        public MemoryPlugin()
        {
        }

        [KernelFunction]
        [Description("Queries the memory for mails")]
        public static async Task<object> GetEmails(
            Kernel kernel,
            [Description("The query")] string query,
            [Description("The amount of results to be fetched")] string amount
        )
        {
            return await kernel.InvokeAsync<object>(
                "memory", "Recall", new()
                {
                    [TextMemoryPlugin.InputParam] = query,
                    [TextMemoryPlugin.CollectionParam] = "emails_test_collection_info",
                    [TextMemoryPlugin.LimitParam] = amount,
                    [TextMemoryPlugin.RelevanceParam] = "0.79",
                });
        }
#pragma warning restore SKEXP0050 
#pragma warning restore SKEXP0001
    }
}
