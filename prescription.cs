using System;

namespace PillPackEx
{
    public  class prescription 
    {  
        public string id {get; set;}
        public string medication_id {get; set;}
        public  DateTime created_at {get; set;}
        public  DateTime updated_at {get; set;}
    }
}