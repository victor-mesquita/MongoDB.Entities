﻿using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MongoDB.Entities
{
    public static partial class DB
    {
        private static readonly BulkWriteOptions unOrdBlkOpts = new BulkWriteOptions { IsOrdered = false };

        /// <summary>
        /// Persists an entity to MongoDB
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        /// <param name="entity">The instance to persist</param>
        /// <param name="session">An optional session if using within a transaction</param>
        /// <param name="cancellation">And optional cancellation token</param>
        public static Task<ReplaceOneResult> SaveAsync<T>(T entity, IClientSessionHandle session = null, CancellationToken cancellation = default) where T : IEntity
        {
            if (string.IsNullOrEmpty(entity.ID))
            {
                entity.SetNewID();
                if (Cache<T>.HasCreatedOn)
                    ((ICreatedOn)entity).CreatedOn = DateTime.UtcNow;
            }

            if (Cache<T>.HasModifiedOn)
                ((IModifiedOn)entity).ModifiedOn = DateTime.UtcNow;

            return session == null
                   ? Collection<T>().ReplaceOneAsync(x => x.ID.Equals(entity.ID), entity, new ReplaceOptions { IsUpsert = true }, cancellation)
                   : Collection<T>().ReplaceOneAsync(session, x => x.ID.Equals(entity.ID), entity, new ReplaceOptions { IsUpsert = true }, cancellation);
        }

        /// <summary>
        /// Persists multiple entities to MongoDB in a single bulk operation
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        /// <param name="entities">The entities to persist</param>
        /// <param name="session">An optional session if using within a transaction</param>
        /// <param name="cancellation">And optional cancellation token</param>
        public static Task<BulkWriteResult<T>> SaveAsync<T>(IEnumerable<T> entities, IClientSessionHandle session = null, CancellationToken cancellation = default) where T : IEntity
        {
            var models = new List<WriteModel<T>>();
            foreach (var ent in entities)
            {
                if (string.IsNullOrEmpty(ent.ID))
                {
                    ent.SetNewID();
                    if (Cache<T>.HasCreatedOn)
                        ((ICreatedOn)ent).CreatedOn = DateTime.UtcNow;
                }

                if (Cache<T>.HasModifiedOn)
                    ((IModifiedOn)ent).ModifiedOn = DateTime.UtcNow;

                var upsert = new ReplaceOneModel<T>(
                        filter: Builders<T>.Filter.Eq(e => e.ID, ent.ID),
                        replacement: ent)
                { IsUpsert = true };
                models.Add(upsert);
            }

            return session == null
                   ? Collection<T>().BulkWriteAsync(models, unOrdBlkOpts, cancellation)
                   : Collection<T>().BulkWriteAsync(session, models, unOrdBlkOpts, cancellation);
        }

        /// <summary>
        /// Saves an entity while preserving some property values in the database.
        /// The properties to be preserved can be specified with a 'New' expression or using the [Preserve] or [DontPreserve] attributes.
        /// <para>TIP: The 'New' expression should specify only root level properties.</para>
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        /// <param name="entity">The entity to save</param>
        /// <param name="preservation">x => new { x.PropOne, x.PropTwo }</param>
        /// <param name="session">An optional session if using within a transaction</param>
        /// <param name="cancellation">An optional cancellation token</param>
        public static Task<UpdateResult> SavePreservingAsync<T>(T entity, Expression<Func<T, object>> preservation = null, IClientSessionHandle session = null, CancellationToken cancellation = default) where T : IEntity
        {
            entity.ThrowIfUnsaved();

            var propsToUpdate = entity.GetType().GetProperties()
                .Where(p =>
                       p.PropertyType.Name != ManyBase.PropType &&
                       !p.IsDefined(typeof(BsonIdAttribute), false) &&
                       !p.IsDefined(typeof(BsonIgnoreAttribute), false) &&
                       !(p.IsDefined(typeof(BsonIgnoreIfDefaultAttribute), false) && p.GetValue(entity) == default) &&
                       !(p.IsDefined(typeof(BsonIgnoreIfNullAttribute), false) && p.GetValue(entity) == null));

            IEnumerable<string> propsToPreserve = default;

            if (preservation == null)
            {
                var dontProps = propsToUpdate.Where(p => p.IsDefined(typeof(DontPreserveAttribute), false)).Select(p => p.Name);
                var presProps = propsToUpdate.Where(p => p.IsDefined(typeof(PreserveAttribute), false)).Select(p => p.Name);

                if (dontProps.Any() && presProps.Any())
                    throw new NotSupportedException("[Preseve] and [DontPreserve] attributes cannot be used together on the same entity!");

                if (dontProps.Any())
                {
                    propsToPreserve = propsToUpdate.Where(p => !dontProps.Contains(p.Name)).Select(p => p.Name);
                }

                if (presProps.Any())
                {
                    propsToPreserve = propsToUpdate.Where(p => presProps.Contains(p.Name)).Select(p => p.Name);
                }

                if (!propsToPreserve.Any())
                    throw new ArgumentException("No properties are being preserved. Please use .Save() method instead!");
            }
            else
            {
                propsToPreserve = (preservation.Body as NewExpression)?.Arguments
                    .Select(a => a.ToString().Split('.')[1]);

                if (!propsToPreserve.Any())
                    throw new ArgumentException("Unable to get any properties from the preservation expression!");
            }

            propsToUpdate = propsToUpdate.Where(p => !propsToPreserve.Contains(p.Name));

            if (!propsToUpdate.Any())
                throw new ArgumentException("At least one property must be not preserved!");

            var defs = new Collection<UpdateDefinition<T>>();

            foreach (var p in propsToUpdate)
            {
                if (p.Name == Cache<T>.ModifiedOnPropName)
                {
                    defs.Add(Builders<T>.Update.CurrentDate(Cache<T>.ModifiedOnPropName));
                }
                else
                {
                    defs.Add(Builders<T>.Update.Set(p.Name, p.GetValue(entity)));
                }
            }

            return
                session == null
                ? Collection<T>().UpdateOneAsync(e => e.ID == entity.ID, Builders<T>.Update.Combine(defs), null, cancellation)
                : Collection<T>().UpdateOneAsync(session, e => e.ID == entity.ID, Builders<T>.Update.Combine(defs), null, cancellation);
        }
    }
}
