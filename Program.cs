using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;

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
        public string id {get; set;}
        public string medication_id {get; set;}
        public  DateTime created_at {get; set;}
        public  DateTime updated_at {get; set;}
    }

    public  class prescription_updates
    {
        public  string prescription_id {get; set;}
        public  string medication_id {get; set;}        
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

    internal enum GenericEquivalentStatus
    {
        IsGeneric,
        GenericEquivalentAvailable,
        NoAvailableEquivalent
    }

    internal struct GenericEquivalentData
    {
        internal GenericEquivalentStatus status{get; set;}
        internal string GenericId  {get; set;}
        
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static string baseurl = "http://api-sandbox.pillpack.com/";

        internal static Serilog.ILogger Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("log.txt")
            .CreateLogger();

        static void Main(string[] args)
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");      
            try{
                  var t1 = DoAsyncWork();
                  t1.Wait();
                  var s1 = t1.Result;
                  var t2 = DoAsyncWork2();
                  t2.Wait();
                  var s2 = t2.Result;
                  
            }
            catch(Exception e)
            {
                Logger.Fatal(e, "Unhandled Exception ");
            }

        }

        internal static async Task<string> DoAsyncWork()
        {
                              
                var scripts =  await GetPrescriptions();
               
                var medsDict =  await BuildMedicationDict();
                var genericCache = new Dictionary<string, GenericEquivalentData>();

                var ScriptsToUpdate = new List<prescription_updates>();
                int callCount = 0;
                foreach (prescription p in scripts)
                {
                    if(!medsDict.ContainsKey(p.medication_id))
                    {
                        Logger.Error($"Medication Id :{p.medication_id } is not a known medication");
                    }
                    else
                    {
                        var med  = medsDict[p.medication_id];
                        if( !med.generic )
                        {

                            var rxcui = med.rxcui;

                            if(genericCache.ContainsKey(rxcui))
                            {
                                
                                var genericData = genericCache[rxcui];
                                if(genericData.status == GenericEquivalentStatus.GenericEquivalentAvailable)
                                {
                                    ScriptsToUpdate.Add(new prescription_updates(){prescription_id = p.id, medication_id = genericCache[rxcui].GenericId});
                                    LogPrescriptionUpdate(p.id, med.id, genericData.GenericId);
                                }                                

                            }
                            else 
                            {
                                callCount++;
                                var  s =  await GetGenericStatus(med );
                                if(!genericCache.ContainsKey(rxcui))
                                {
                                    genericCache.Add(rxcui, s);
                                }
                                if(s.status == GenericEquivalentStatus.GenericEquivalentAvailable)
                                {           
                                    ScriptsToUpdate.Add(new prescription_updates(){prescription_id = p.id, medication_id = s.GenericId});
                                    LogPrescriptionUpdate(p.id, med.id, s.GenericId);
                                }
                            }
                        }
                    }
                    
                }
                                
                return JsonConvert.SerializeObject(ScriptsToUpdate, Formatting.Indented);
        }

        internal static async Task<string> DoAsyncWork2()
        {

            var scripts = await GetPrescriptions();
            var medsDict = await BuildMedicationDict();
            var genericCache = new Dictionary<string, GenericEquivalentData>();
            BuildGenericsCache(medsDict, genericCache);

            var ScriptsToUpdate = new List<prescription_updates>();
            foreach (prescription p in scripts)
            {
                if (!medsDict.ContainsKey(p.medication_id))
                {
                    Console.WriteLine(String.Format($"MedicationNot Found Id:{p.medication_id}"));
                }
                else
                {
                    var med = medsDict[p.medication_id];
                    if (!med.generic)
                    {

                        var rxcui = med.rxcui;

                        if (genericCache.ContainsKey(rxcui))
                        {
                            var genericData = genericCache[rxcui];
                            if (genericData.status == GenericEquivalentStatus.GenericEquivalentAvailable)
                            {
                                ScriptsToUpdate.Add(new prescription_updates() { prescription_id = p.id, medication_id = genericCache[rxcui].GenericId });
                                LogPrescriptionUpdate(p.id, med.id, genericData.GenericId);
                            }
                        }
                        else
                        {
                            LogPrescriptionWithNoUpdate( p.id, med.id);

                        }
                    }
                }

            }

            return JsonConvert.SerializeObject(ScriptsToUpdate, Formatting.Indented);
        }

        private static void LogPrescriptionUpdate( string script_id, string med_id, string gen_id)
        {
            Logger.Debug($"Adding Script Update script_id:{script_id}; med_id:{med_id}; gen_id:{gen_id}");
        }
        private static void LogPrescriptionWithNoUpdate( string script_id, string med_id)
        {
            Logger.Debug($"No generic med for script_id:{script_id}; med_id:{med_id};");
        }

        private static void BuildGenericsCache(Dictionary<string, medication> medsDict, Dictionary<string, GenericEquivalentData> genericCache)
        {
            foreach (var m in medsDict)
            {
                var med = m.Value;
                if (med.generic && med.active)
                {
                   
                   try
                   {
                       genericCache.Add(med.rxcui, new GenericEquivalentData() { status = GenericEquivalentStatus.GenericEquivalentAvailable, GenericId = med.id });
                   }
                   catch(Exception e)
                    {
                        Logger.Error(e, "Duplicate RXCUI Active Generic in medications ");
                    }
                }
            }
        }

        internal static async Task<List<medication>> GetEquvailentMedications(string rxcui)
        {
                string logPrefix ="GetEquvilentMedications:"; 
                if(string.IsNullOrEmpty(rxcui))
                {
                    throw new Exception("Parameter rxcui must be have a valid value");
                }
                //httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
                var url = string.Format($"{baseurl}medications?rxcui={rxcui}");
                
                Logger.Debug( $"{logPrefix} url:{url}");
                using (var client = new HttpClient())
                {
                    var response = await httpClient.GetAsync(new Uri(url));
                
                    Logger.Debug( $"{logPrefix} response code: {response.StatusCode}" );

                    var r = await response.Content.ReadAsStringAsync();
                    var meds = JsonConvert.DeserializeObject<List<medication>>(r);
                    return meds;
                
                }
                       
        }
        internal static async Task<List<prescription>> GetPrescriptions()
        {
                List<string> errors = new List<string>();
                var url = string.Format($"{baseurl}prescriptions");
                var response = await httpClient.GetStringAsync(new Uri(url));
                var scripts = JsonConvert.DeserializeObject<List<prescription>>(response);  
                return scripts;                      
        }
        internal static async Task<Dictionary<string,medication>> BuildMedicationDict()
        {
                List<string> errors = new List<string>();
                //httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
                var url = string.Format($"{baseurl}medications");
                var response = await httpClient.GetStringAsync(new Uri(url));
                var meds = JsonConvert.DeserializeObject<List<medication>>(response);
                return meds.ToDictionary(x => x.id);       
        }

        // internal static async Task<bool> GetStatusAndAddToUpdate (prescription prescription,  List<prescription_updates> updateList, Dictionary<string, medication> meds, int count)
        // {
        //     try
        //     {
        //         var s = await GetGenericStatus(meds, prescription.medication_id);
        //         if(s.status == GenericEquivalentStatus.GenericEquivalentAvailable)
        //         {
        //             updateList.Add(new prescription_updates(){prescription_id=prescription.id, medication_id = s.GenericId});
        //             return true;
        //         }
        //     }
        //     catch
        //     {
        //         if(count<5)
        //         {
        //             System.Console.WriteLine("Retrying");
        //             ++count;
        //             return await GetStatusAndAddToUpdate(prescription, updateList, meds, count);

        //         }
        //         System.Console.WriteLine("Out of Retries Unable to upate Prescription:"+ prescription.id );

        //         return false;

        //     }
        //     return false;

        // }
        
        internal static async Task<GenericEquivalentData> GetGenericStatus (medication med)
        {
            
            if(med.generic && med.active)
            {
                return new GenericEquivalentData(){status =  GenericEquivalentStatus.IsGeneric, GenericId= med.id};
            }
    
            try
            {
                var equivalentMedsResult = await GetEquvailentMedications(med.rxcui);
                var equivalentMeds  = equivalentMedsResult.Where( x => (x.generic = true) && (x.active = true) && (x.id != med.id));
                if(!equivalentMeds.Any())
                {
                    return new GenericEquivalentData(){status =  GenericEquivalentStatus.NoAvailableEquivalent, GenericId= med.id};
                }

                var firstEquivalent = equivalentMeds.First();
                return new GenericEquivalentData(){status =  GenericEquivalentStatus.GenericEquivalentAvailable, GenericId= firstEquivalent.id };
            }
            catch(Exception e)
            {
                // consider using a different status to indicate error
                return new GenericEquivalentData(){status =  GenericEquivalentStatus.NoAvailableEquivalent, GenericId= med.id};
            }
            
            
        }
    }

}
