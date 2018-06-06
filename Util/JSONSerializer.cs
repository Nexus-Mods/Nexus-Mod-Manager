using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.ComponentModel;

namespace Nexus.Client.Util
{
    public static class JSONSerializer
    {
        public static string Serialize<T>(T aObject) where T : new()
        {
            T serializedObj = new T();
            MemoryStream ms = new MemoryStream();
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
            ser.WriteObject(ms, serializedObj);
            byte[] json = ms.ToArray();
            ms.Close();
            return Encoding.UTF8.GetString(json, 0, json.Length);
        }

        public static T Deserialize<T>(string aJSON) where T : new()
        {
            T deserializedObj = default(T);
            MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(aJSON));
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
            deserializedObj = (T)ser.ReadObject(ms);
            ms.Close();
            return deserializedObj;
        }
    }
}
