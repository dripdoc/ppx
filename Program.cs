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
using System.Net;

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
                  var t1 = DoAsyncWorkLiveRequests();
                  t1.Wait();
                  var s1 = t1.Result;

                  var t2 = DoAsyncWorkUseStaticDataFromMedications();
                  t2.Wait();
                  var s2 = t2.Result;

                  Console.WriteLine("S1 output");
                  Console.Write(s1);
                  Console.WriteLine("S2 output");
                  Console.Write(s2);

                  Console.WriteLine("");
                  Console.WriteLine ($"s1 output == s2 output :{s1==s2}");
            }
            catch(Exception e)
            {
                Logger.Fatal(e, "Unhandled Exception ");
            }

        }

        internal static async Task<string> DoAsyncWorkLiveRequests()
        {
            // would be used in the case that updates to generics occur in real time 
            // that they could change between the time the                
            var scripts =  await GetPrescriptions();
            
            // consider using concurrent dictionary
            var genericCache = new Dictionary<string, GenericEquivalentData>();
            var medCache = new ConcurrentDictionary<string, medication>();

            var ScriptsToUpdate = new List<prescription_updates>();
            int callCount = 0;
            foreach (prescription p in scripts)
            {
                medication med = null;
                string med_id = p.medication_id;
                if(medCache.ContainsKey(med_id))
                    {
                    med = medCache[med_id];
                    }   
                else
                {
                    med  = await  GetMedication(med_id);
                }
                
                if(med == null)
                {
                    LogMedicationNotFoundLive( med_id);
                }
                else
                {
                    medCache.AddOrUpdate(med_id, med, (k, m1 )=>{return m1;});
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

        internal static async Task<string> DoAsyncWorkUseStaticDataFromMedications()
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
                    LogMedicationNotFound( p.medication_id);
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

        private static void LogMedicationNotFound(string med_id)
        {
            Logger.Debug(String.Format($"Medication Not Found Id:{med_id}"));
        }
        private static void LogMedicationNotFoundLive(string med_id)
        {
            Logger.Debug(String.Format($"LiveMedication Not Found Id:{med_id}"));
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
            var response = await httpClient.GetAsync(new Uri(url));
            
            if(response.StatusCode != HttpStatusCode.OK)
            {
                    Logger.Debug( $"{logPrefix} response url:{url}; code: {response.StatusCode}" );
                return null;
            }
            var r = await response.Content.ReadAsStringAsync();
            var meds = JsonConvert.DeserializeObject<List<medication>>(r);
            return meds;                       
        }
        internal static async Task<List<prescription>> GetPrescriptions()
        {
            List<string> errors = new List<string>();
            var url = string.Format($"{baseurl}prescriptions");
            var response = await httpClient.GetAsync(new Uri(url));
            if(response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Debug( $"GetPrescriptions Failed http Request response url:{url}; code: {response.StatusCode}" );
                return null;        
            }
            var content = await response.Content.ReadAsStringAsync();
            var scripts = JsonConvert.DeserializeObject<List<prescription>>(content);  
            return scripts;                      
        }

        internal static async Task<medication> GetMedication(string id)
        {
            string logPrefix ="GetMedication:";     
            List<string> errors = new List<string>();
            var url = string.Format($"{baseurl}medications/{id}");
            
            Logger.Debug( $"{logPrefix} url:{url}");
            
            var response = await httpClient.GetAsync(new Uri(url));
        
            if(response.StatusCode != HttpStatusCode.OK)
            {
                 Logger.Debug( $"{logPrefix} response url:{url}; code: {response.StatusCode}" );
                return null;
            }

            var r = await response.Content.ReadAsStringAsync();
            var medication = JsonConvert.DeserializeObject<medication>(r);
            
            return medication;                      
        }
        internal static async Task<Dictionary<string,medication>> BuildMedicationDict()
        {
            List<string> errors = new List<string>();
            //httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
            var url = string.Format($"{baseurl}medications");
            var response = await httpClient.GetAsync(new Uri(url));
            if(response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Debug( $"BuildMedicationDict Failed http Request url:{url} response code: {response.StatusCode}" );        
            }
            var content = await response.Content.ReadAsStringAsync();
            var meds = JsonConvert.DeserializeObject<List<medication>>(content);
            return meds.ToDictionary(x => x.id);           
        }

        
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
                Logger.Debug(e, "Error Attempting to get equivalent generic");
                return new GenericEquivalentData(){status =  GenericEquivalentStatus.NoAvailableEquivalent, GenericId= med.id};
            }
            
            
        }
    }

}
