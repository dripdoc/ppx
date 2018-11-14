using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Numerics;

namespace PillPackEx
{
    public  class medication  
    {  
        public  string id {get; set;}
        public  string ndc {get; set;}
        public  string rxcui {get; set;}
        public  string description {get; set;}
        public  bool   generic {get; set;}
        public  bool active {get; set;}
        public DateTime created_at {get; set;}
        public DateTime updated_at {get; set;}
    }

    public  class prescription 
    {  
        public BigInteger id {get; set;}
        public BigInteger medication_id {get; set;}
        public  DateTime created_at {get; set;}
        public  DateTime updated_at {get; set;}
    }

    internal class prescription_updates
    {
        internal string prescription_id {get; set;}
        internal string medication_id {get; set;}        
    }
//     internal class IdConverter : JsonConverter<decimal>
//     {
//         public override void WriteJson(JsonWriter writer, decimal value, JsonSerializer serializer)
//         {
//             writer.WriteValue(value.ToString());
//         }

//         public override decimal ReadJson(JsonReader reader, Type objectType, decimal existingValue, bool hasExistingValue, JsonSerializer serializer)
//         {
//             string s = (string)reader.Value;

//             return new decimal();
//         }
// }

    internal enum GenericStatus
    {
        IsGeneric,
        GenericEquivalentAvailable,
        NoAvailableEquivalent
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static string baseurl = "http://api-sandbox.pillpack.com/";
        static void Main(string[] args)
        {
           
            try{
                  var t = DoAsyncWork();
                  t.Wait();
                  var s = t.Result;
            }
            catch(Exception e)
            {
                Console.WriteLine("Fatal Error "+ e.Message);
            }

            //var releases = JArray.Parse(response);
        }

        internal static async Task<string> DoAsyncWork()
        {
               
                var scripts =  await GetPrescriptions();
               
                var medsDict =  await BuildMedicationDict();
                
                var ScriptsToUpdate = new List<prescription_updates>();
                foreach (prescription p in scripts)
                {
                    //var genericStatus = await GetGenericStatus(medsDict, p.medication_id);
                    // if(genericStatus.Item1 == GenericStatus.GenericEquivalentAvailable)
                    // {
                    //     //ScriptsToUpdate.Add(new prescription_updates( ){prescription_id = p.id, medication_id = genericStatus.Item2});
                    // }

                }
                return JsonConvert.SerializeObject(ScriptsToUpdate);
        }

        internal static async Task<List<medication>> GetEquvailentMedications(string rxcui)
        {
                if(string.IsNullOrEmpty(rxcui))
                {
                    throw new Exception("Parameter rxcui must be have a valid value");
                }
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
                var url = string.Format($"{baseurl}medications?rxcui={rxcui}");
                var response = await httpClient.GetStringAsync(new Uri(url));
                var meds = JsonConvert.DeserializeObject<List<medication>>(response);
                return meds;
                       
        }
        internal static async Task<List<prescription>> GetPrescriptions()
        {
                List<string> errors = new List<string>();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
                var url = string.Format($"{baseurl}prescriptions");
                var response = await httpClient.GetStringAsync(new Uri(url));

                var rs = "["+
  "{"+
    "\"id\": \"564aab713032360003280000\","+
    "\"medication_id\": \"564aab703032360003130000\","+
    "\"created_at\": \"2015-11-17T04:22:09.752Z\","+
    "\"updated_at\": \"2015-11-17T04:22:09.752Z\""+
  "},"+
  "{"+
    "\"id\": \"564aab713032360003290000\","+
    "\"medication_id\": \"564aab713032360003260000\","+
    "\"created_at\": \"2015-11-17T04:22:09.795Z\","+
    "\"updated_at\": \"2015-11-17T04:22:09.795Z\""+
  "},"+
  "{"+
    "\"id\": \"564aab7130323600032a0000\","+
    "\"medication_id\": \"564aab6f30323600030c0000\","+
    "\"created_at\": \"2015-11-17T04:22:09.832Z\","+
    "\"updated_at\": \"2015-11-17T04:22:09.832Z\", "+
  "}]";

                ITraceWriter traceWriter = new MemoryTraceWriter();

                var scripts = JsonConvert.DeserializeObject<List<prescription>>(rs,
                 new JsonSerializerSettings
                {
                TraceWriter = traceWriter,
                Error = delegate(object sender, ErrorEventArgs args)
                    {
                        errors.Add(args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    },
                    Converters = { new IsoDateTimeConverter() }
                });
                
                System.Console.WriteLine(traceWriter);

                System.Console.WriteLine(scripts[0].id);
                return scripts;                      
        }
        internal static async Task<Dictionary<string,medication>> BuildMedicationDict()
        {
                List<string> errors = new List<string>();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
                var url = string.Format($"{baseurl}medications");
                var response = await httpClient.GetStringAsync(new Uri(url));
                var meds = JsonConvert.DeserializeObject<List<medication>>(response,
                 new JsonSerializerSettings
                {
                Error = delegate(object sender, ErrorEventArgs args)
                    {
                        errors.Add(args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    },
                    Converters = { new IsoDateTimeConverter() }
                });
                return meds.ToDictionary(x => x.id);       
        }

        internal static async Task<Tuple<GenericStatus, string>> GetGenericStatus (Dictionary<string, medication> meds, string m_id)
        {
            if(!meds.ContainsKey(m_id))
            {
                throw new Exception(string.Format($"Medication:{m_id} is not found"));
            }
            
            var prescribedMed = meds[m_id];
            if(prescribedMed.generic)
            {
                return Tuple.Create(GenericStatus.IsGeneric, m_id);
            }

            var equivalentMedsResult = await GetEquvailentMedications(prescribedMed.rxcui);
            var equivalentMeds  = equivalentMedsResult.Where( x => x.generic = true);
            if(!equivalentMeds.Any())
            {
                return Tuple.Create(GenericStatus.NoAvailableEquivalent, m_id);
            }

            return Tuple.Create(GenericStatus.GenericEquivalentAvailable, equivalentMeds.First().id);
            
        }
    }

}
