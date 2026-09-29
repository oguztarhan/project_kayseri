using System;
using System.Reflection;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopCustomerViewTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _host;
        private MiningShopView _view;
        private MiningShopState _state;
        private MiningShopBusinessSimulation _sim;
        private Array _customers;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("CustomerViewTest");
            _view = _host.AddComponent<MiningShopView>();
            Set("_shelf", Child("Shelf", Vector3.zero));
            Set("_queue", new[] { Child("Serve", Vector3.zero), Child("Queue1", Vector3.back * 20f),
                Child("Queue2", Vector3.back * 40f) });
            Type walker = typeof(MiningShopView).GetNestedType("Walker", BindingFlags.NonPublic);
            _customers = Array.CreateInstance(walker, 3);
            for (int i = 0; i < _customers.Length; i++)
            {
                object person = Activator.CreateInstance(walker, true);
                Transform body = Child("Customer" + i, Vector3.zero);
                walker.GetField("body").SetValue(person, body);
                var held = new Transform[MiningShopCampaign.ProductCount];
                for (int p = 0; p < held.Length; p++)
                {
                    held[p] = Child("Held" + p, Vector3.zero);
                    held[p].SetParent(body, false);
                    held[p].gameObject.SetActive(false);
                }
                walker.GetField("held").SetValue(person, held);
                body.gameObject.SetActive(false);
                _customers.SetValue(person, i);
            }
            Set("_customers", _customers);
            Set("_waiting", new int[_customers.Length]);
            _state = new MiningShopState { BusinessId = "customer-view-test" };
            var tuning = MiningShopBusinessSimulation.Tuning.Default;
            tuning.BuildRequiresLevel = 1;
            _sim = new MiningShopBusinessSimulation(_state, 2, tuning, _ => { });
            Assert.That(_sim.BuildTable(1), Is.True);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_host);

        private Transform Child(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_host.transform, false);
            go.transform.position = position;
            return go.transform;
        }

        private void Set(string field, object value) => typeof(MiningShopView).GetField(field, Private).SetValue(_view, value);
        private void Call(string method, params object[] args) => typeof(MiningShopView).GetMethod(method, Private | BindingFlags.Static).Invoke(_view, args);
        private void Reconcile() => Call("Reconcile", _sim.View);
        private T Read<T>(int customer, string field)
        {
            object walker = _customers.GetValue(customer);
            return (T)walker.GetType().GetField(field).GetValue(walker);
        }

        private int CustomerFor(int product)
        {
            for (int i = 0; i < _customers.Length; i++)
                if (_view.TryGetCustomerRequest(i, out _, out int requested, out _) && requested == product) return i;
            return -1;
        }

        [Test]
        public void RequestsStayWithCustomersAndTheMatchingProductIsServed()
        {
            _state.Business.Lines[0].WaitingCustomers = 2;
            _state.Business.Lines[1].WaitingCustomers = 1;
            Reconcile();
            int pickaxe = CustomerFor(0), helmet = CustomerFor(1);
            Assert.That(pickaxe, Is.GreaterThanOrEqualTo(0));
            Assert.That(helmet, Is.GreaterThanOrEqualTo(0));
            Reconcile();
            Assert.That(CustomerFor(0), Is.EqualTo(pickaxe));
            Assert.That(CustomerFor(1), Is.EqualTo(helmet));
            _state.Business.Lines[1].WaitingCustomers = 0;
            _state.Business.Serving = true;
            _state.Business.ServiceProductIndex = 1;
            Reconcile();
            Assert.That((int)typeof(MiningShopView).GetField("_serving", Private).GetValue(_view), Is.EqualTo(helmet));
            Assert.That(Read<int>(helmet, "heldProduct"), Is.EqualTo(1));
            Assert.That(Read<int>(pickaxe, "request"), Is.EqualTo(0));
        }

        [Test]
        public void OnlyOnePickaxeRequestIsHighlightedAndPoolExhaustionIsSafe()
        {
            _state.Business.Lines[0].WaitingCustomers = 2;
            _state.Business.Lines[1].WaitingCustomers = 2;
            Reconcile();
            int shown = 0, highlighted = 0;
            for (int i = 0; i < _customers.Length; i++)
                if (_view.TryGetCustomerRequest(i, out _, out int product, out bool focus))
                {
                    shown++;
                    if (focus) { highlighted++; Assert.That(product, Is.Zero); }
                }
            Assert.That(shown, Is.EqualTo(3));
            Assert.That(highlighted, Is.EqualTo(1));
        }

        [Test]
        public void LeavingCustomerStopsRequestingAndCelebratesBeforeWalking()
        {
            _state.Business.Serving = true;
            _state.Business.ServiceProductIndex = 0;
            Reconcile();
            int customer = CustomerFor(0);
            Transform body = Read<Transform>(customer, "body");
            Vector3 before = body.position;
            Call("OnSold", FirstReceipt());
            Assert.That(_view.TryGetCustomerRequest(customer, out _, out _, out _), Is.False);
            Assert.That(Read<float>(customer, "hop"), Is.GreaterThan(0f));
            Call("WalkCustomers", 0.1f);
            Assert.That(body.position, Is.EqualTo(before));
            Call("WalkCustomers", 1f);
            Assert.That(body.localScale, Is.EqualTo(Vector3.one));
            Assert.That(body.position, Is.Not.EqualTo(before));
        }

        [Test]
        public void ReducedMotionSkipsReceiptAnimationAndReusedBodyHasNoOldRequest()
        {
            var accessibility = ScriptableObject.CreateInstance<AccessibilityConfig>();
            try
            {
                typeof(AccessibilityConfig).GetField("reduceMotion", Private).SetValue(accessibility, true);
                Set("_accessibility", accessibility);
                _state.Business.Serving = true;
                _state.Business.ServiceProductIndex = 0;
                Reconcile();
                int customer = CustomerFor(0);
                Call("OnSold", FirstReceipt());
                Assert.That(Read<float>(customer, "hop"), Is.Zero);
                Call("Hide", _customers.GetValue(customer));
                Assert.That(Read<int>(customer, "request"), Is.EqualTo(-1));
                Assert.That(Read<int>(customer, "heldProduct"), Is.EqualTo(-1));
            }
            finally { UnityEngine.Object.DestroyImmediate(accessibility); }
        }

        private static MiningShopBusinessSimulation.Sale FirstReceipt()
        {
            MiningShopBusinessSimulation.Sale receipt = default;
            bool received = false;
            var sim = new MiningShopBusinessSimulation(new MiningShopState { BusinessId = "receipt-test" }, 1,
                MiningShopBusinessSimulation.Tuning.Default, sale => { receipt = sale; received = true; });
            for (int i = 0; i < 100 && !received; i++) sim.Advance(1d);
            Assert.That(received, Is.True);
            return receipt;
        }
    }
}
