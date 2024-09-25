using System.Net.Http.Headers;

namespace LanguageModels;

class Groq(GroqHttpClient httpClient, string model) : VanillaOpenAI(httpClient.HttpClient, model)
{
    public override bool SupportImageInputs => false;
}