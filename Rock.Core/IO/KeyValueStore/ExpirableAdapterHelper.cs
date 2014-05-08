﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rock.IO
{
    public class ExpirableAdapterHelper
    {
        private const int _timerDueTime = 30000;
        private readonly Timer _timer;

        private readonly ConcurrentDictionary<int, ExpirableBucketItemAdapter> _expirableItems = new ConcurrentDictionary<int, ExpirableBucketItemAdapter>();
        private readonly BlockingCollection<ExpirableBucketItemAdapter> _itemsToRemove = new BlockingCollection<ExpirableBucketItemAdapter>();

        public ExpirableAdapterHelper()
        {
            Task.Factory.StartNew(RemoveExpiredItems);
            _timer = new Timer(CheckForExpiredItems);
            _timer.Change(_timerDueTime, Timeout.Infinite);
        }

        public void RegisterItem(ExpirableBucketItemAdapter item)
        {
            _expirableItems.AddOrUpdate(
                GetKey(item),
                item,
                (key, adapter) => item);
        }

        public void RemoveItem(ExpirableBucketItemAdapter item)
        {
            _expirableItems.TryRemove(GetKey(item), out item);
        }

        private void RemoveExpiredItems()
        {
            foreach (var itemToRemove in _itemsToRemove.GetConsumingEnumerable())
            {
                itemToRemove.Delete();
                RemoveItem(itemToRemove);
            }
        }

        private void CheckForExpiredItems(object state)
        {
            try
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);

                var now = DateTime.UtcNow;

                foreach (var expirableItem in
                    _expirableItems.Values.Where(expirableItem => now > expirableItem.GetExpirationDate()))
                {
                    _itemsToRemove.Add(expirableItem);
                }
            }
            finally
            {
                _timer.Change(_timerDueTime, Timeout.Infinite);
            }
        }

        private static int GetKey(IBucketItem item)
        {
            return ((item.BucketName != null ? item.BucketName.GetHashCode() : 0) * 397) ^ item.Key.GetHashCode();
        }
    }
}