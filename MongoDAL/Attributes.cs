﻿using MongoDB.Bson.Serialization.Attributes;
using System;

namespace MongoDAL
{
    /// <summary>
    /// Indicates that this field or property should be ignored when this class is persisted to MongoDB.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class MongoIgnoreAttribute : BsonIgnoreAttribute { }
}
