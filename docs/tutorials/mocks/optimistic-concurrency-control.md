## Simulating optimistic concurrency control using ETags

Concurrency unit testing with Coyote often involves writing mocks that _simulate_ (a subset of) the
behavior of an external service or library. This is a "pay-as-you-go" effort, it is up to you to
decide how simple or complex you want your mocks to be depending on what kind of logic you want to
test! You can start with writing some very simple mocks and incrementally add behavior if you want
to test more advanced scenarios. The only requirement is that the mocks must work on a concurrent
setting, as Coyote [explores interleavings and other sources of
nondeterminism](../../concepts/non-determinism.md).

For example, the simple `InMemoryDbCollection` mock described in this
[tutorial](mock-dependencies.md) simulates asynchronous row manipulation in a backend NoSQL database
to [test the logic](../first-concurrency-unit-test.md) of an `AccountManager` controller. A great
benefit of designing such a mock is that it can be reused across [many different concurrency unit
tests](../test-concurrent-operations.md), comparing comparing to the more traditional approach of
writing very simple mock methods that return fixed results (like in the [first
version](mock-dependencies.md) of the `InMemoryDbCollection` mock).

In this tutorial, you will see that it is very easy to take this `InMemoryDbCollection` mock and
extend it with [ETags](https://en.wikipedia.org/wiki/HTTP_ETag) to simulate [optimistic concurrency
control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control). While the implementation of
an actual NoSQL database can be really complex, enhancing our mock with ETag semantics can be fairly
trivial.

## What you will need

To run the code in this tutorial, you will need to:

- Install [Visual Studio 2019](https://visualstudio.microsoft.com/downloads/).
- Install the [.NET 5.0 version of the coyote tool](../../get-started/install.md).
- Be familiar with the `coyote` tool. See [using Coyote](../../get-started/using-coyote.md).
- Go through the [mocking dependencies for testing](mock-dependencies.md) tutorial.

## Walkthrough

Let's motivate the problem by extending our `AccountManager` controller to support updating existing
accounts. An account can only be updated if the version of the new instance is greater than that of
the existing instance. To deal with this design requirement, the `AccountManager` must now maintain
a version per account (besides its name and payload), so let's make our life easier and define the
following `Account` class, which includes a `Version` property.

```csharp
public class Account
{
  public string Name { get; set; }

  public string Payload { get; set; }

  public int Version { get; set; }
}
```

Recall that accounts are stored in a backend NoSQL database, which the `AccountManager` accesses via
the `IDbCollection` interface. To be able to update stored accounts, extend `IDbCollection` with an
`UpdateRow` method.

```csharp
public interface IDbCollection
{
    Task<bool> CreateRow(string key, string value);

    Task<bool> DoesRowExist(string key);

    Task<string> GetRow(string key);

    Task<bool> UpdateRow(string key, string value);

    Task<bool> DeleteRow(string key);
}
```

You will also need to extend the `InMemoryDbCollection` mock with `UpdateRow`. Let's write a very
simple mock implementation for this method.

```csharp
public class InMemoryDbCollection : IDbCollection
{
  private readonly ConcurrentDictionary<string, string> Collection;

  public InMemoryDbCollection()
  {
    this.Collection = new ConcurrentDictionary<string, string>();
  }

  public Task<bool> CreateRow(string key, string value)
  {
    return Task.Run(() =>
    {
      bool success = this.Collection.TryAdd(key, value);
      if (!success)
      {
        throw new RowAlreadyExistsException();
      }

      return true;
    });
  }

  public Task<bool> DoesRowExist(string key)
  {
    return Task.Run(() =>
    {
      return this.Collection.ContainsKey(key);
    });
  }

  public Task<string> GetRow(string key)
  {
    return Task.Run(() =>
    {
      bool success = this.Collection.TryGetValue(key, out string value);
      if (!success)
      {
        throw new RowNotFoundException();
      }

      return value;
    });
  }

  public Task<bool> UpdateRow(string key, string value)
  {
    return Task.Run(() =>
    {
      bool success = this.Collection.ContainsKey(key);
      if (!success)
      {
        throw new RowNotFoundException();
      }

      this.Collection[key] = value;
      return true;
    });
  }

  public Task<bool> DeleteRow(string key)
  {
    return Task.Run(() =>
    {
      bool success = this.Collection.TryRemove(key, out string _);
      if (!success)
      {
        throw new RowNotFoundException();
      }

      return true;
    });
  }
}
```

Next, let's implement the `AccountManager` logic.

```csharp
public class AccountManager
{
  private readonly IDbCollection AccountCollection;

  public AccountManager(IDbCollection dbCollection)
  {
    this.AccountCollection = dbCollection;
  }

  // Returns true if the account is created, else false.
  public async Task<bool> CreateAccount(string accountName, string accountPayload, int accountVersion)
  {
    var account = new Account()
    {
      Name = accountName,
      Payload = accountPayload,
      Version = accountVersion
    };

    try
    {
      return await this.AccountCollection.CreateRow(accountName, JsonSerializer.Serialize(account));
    }
    catch (RowAlreadyExistsException)
    {
      return false;
    }
  }

  // Returns true if the account is updated, else false.
  public async Task<bool> UpdateAccount(string accountName, string accountPayload, int accountVersion)
  {
    Account existingAccount;

    try
    {
      string value = await this.AccountCollection.GetRow(accountName);
      existingAccount = JsonSerializer.Deserialize<Account>(value);
    }
    catch (RowNotFoundException)
    {
      return false;
    }

    if (accountVersion <= existingAccount.Version)
    {
      return false;
    }

    var updatedAccount = new Account()
    {
      Name = accountName,
      Payload = accountPayload,
      Version = accountVersion
    };

    try
    {
      return await this.AccountCollection.UpdateRow(accountName, JsonSerializer.Serialize(updatedAccount));
    }
    catch (RowNotFoundException)
    {
      return false;
    }
  }

  // Returns the account if found, else null.
  public async Task<Account> GetAccount(string accountName)
  {
    try
    {
      string value = await this.AccountCollection.GetRow(accountName);
      return JsonSerializer.Deserialize<Account>(value);
    }
    catch (RowNotFoundException)
    {
      return null;
    }
  }

  // Returns true if the account is deleted, else false.
  public async Task<bool> DeleteAccount(string accountName)
  {
    try
    {
      return await this.AccountCollection.DeleteRow(accountName);
    }
    catch (RowNotFoundException)
    {
      return false;
    }
  }
}
```

This was a lot of code!

The `CreateAccount` is similar to the [previous tutorial](../first-concurrency-unit-test.md), but
with a few differences. It creates an `Account` instance using the input account data, then uses
`System.Text.Json` to serialize it to a `string` and tries to add it to the database by invoking
`CreateRow`. If this operation fails with a `RowAlreadyExistsException`, the `AccountManager`
catches the exception and returns `false`, else it returns `true`.

The `UpdateAccount` method is a bit more involved. The method first invokes the `GetRow` database
method to get the value of the account with the name that we want to update (if such an account
already exists), and uses `System.Text.Json` to deserialize the returned value to an `Account`
instance. Next, the `AccountManager` checks if the version of the existing account is greater or
equal than the new account, and if yes, the method fails with `false`. Else, it creates a new
`Account` instance, serializes it and tries to update the corresponding database entry by invoking
`UpdateRow`.

The `GetAccount` and `DeleteAccount` methods are also similar to the [previous
tutorial](../first-concurrency-unit-test.md), but now use a `try { ... } catch { ... }` block to
return `false` if the call to `IDbCollection` failed with a `RowNotFoundException`.
 
Let's first write a sequential unit test to exercise the above `UpdateAccount` logic.

```csharp
[Microsoft.Coyote.SystematicTesting.Test]
public static async Task TestAccountUpdate()
{
  // Initialize the mock in-memory DB and account manager.
  var dbCollection = new InMemoryDbCollection();
  var accountManager = new AccountManager(dbCollection);

  string accountName = "MyAccount";

  // Create the account, it should complete successfully and return true.
  var result = await accountManager.CreateAccount(accountName, "first_version", 1);
  Assert.True(result);

  result = await accountManager.UpdateAccount(accountName, "second_version", 2);
  Assert.True(result);

  result = await accountManager.UpdateAccount(accountName, "second_version_alt", 2);
  Assert.False(result);
}
```

Build the code, rewrite the assembly and run the test using Coyote for `10` iterations:

```plain
coyote rewrite .\AccountManager.ETags.dll
coyote test .\AccountManager.ETags.dll -m TestAccountUpdate -i 10
```

The test succeeds.

```plain
. Testing .\AccountManager.ETags.dll
... Method TestAccountUpdate
... Started the testing task scheduler (process:37236).
... Created '1' testing task (process:37236).
... Task 0 is using 'random' strategy (seed:2049239085).
..... Iteration #1
..... Iteration #2
..... Iteration #3
..... Iteration #4
..... Iteration #5
..... Iteration #6
..... Iteration #7
..... Iteration #8
..... Iteration #9
..... Iteration #10
... Testing statistics:
..... Found 0 bugs.
... Scheduling statistics:
..... Explored 10 schedules: 10 fair and 0 unfair.
..... Number of scheduling points in fair terminating schedules: 15 (min), 17 (avg), 25 (max).
... Elapsed 0.2354834 sec.
```

This is cool, but will a test that exercises concurrent account updates also succeed? Let's find out
by writing the following concurrency unit test.

```csharp
[Microsoft.Coyote.SystematicTesting.Test]
public static async Task TestConcurrentAccountUpdate()
{
  // Initialize the mock in-memory DB and account manager.
  var dbCollection = new InMemoryDbCollection();
  var accountManager = new AccountManager(dbCollection);

  string accountName = "MyAccount";

  // Create the account, it should complete successfully and return true.
  var result = await accountManager.CreateAccount(accountName, "first_version", 1);
  Assert.True(result);

  // Call UpdateAccount twice without awaiting, which makes both methods run
  // asynchronously with each other.
  var task1 = accountManager.UpdateAccount(accountName, "second_version", 2);
  var task2 = accountManager.UpdateAccount(accountName, "second_version_alt", 2);

  // Then wait both requests to complete.
  await Task.WhenAll(task1, task2);

  // Finally, assert that only one of the two requests succeeded and the other
  // failed. Note that we do not know which one of the two succeeded as the
  // requests ran concurrently (this is why we use an exclusive OR).
  Assert.True(task1.Result ^ task2.Result);
}
```

Build the code, rewrite the assembly and run the test using Coyote for `10` iterations:

```plain
coyote rewrite .\AccountManager.ETags.dll
coyote test .\AccountManager.ETags.dll -m TestConcurrentAccountUpdate -i 10
```

When you run the test above, you'll realize it will fail quite fast as Coyote will find an execution
in which _both_ `UpdateAccount` requests succeed.

```plain
. Testing .\AccountManager.ETags.dll
... Method TestConcurrentAccountUpdate
... Started the testing task scheduler (process:35908).
... Created '1' testing task (process:35908).
... Task 0 is using 'random' strategy (seed:3363370498).
..... Iteration #1
..... Iteration #2
..... Iteration #3
..... Iteration #4
..... Iteration #5
..... Iteration #6
... Task 0 found a bug.
... Emitting task 0 traces:
..... Writing AccountManager.ETags.dll\CoyoteOutput\AccountManager.ETags_0_0.txt
..... Writing AccountManager.ETags.dll\CoyoteOutput\AccountManager.ETags_0_0.schedule
... Elapsed 0.1426821 sec.
... Testing statistics:
..... Found 1 bug.
... Scheduling statistics:
..... Explored 6 schedules: 6 fair and 0 unfair.
..... Found 16.67% buggy schedules.
..... Number of scheduling points in fair terminating schedules: 10 (min), 19 (avg), 28 (max).
... Elapsed 0.2414493 sec.
```

This is a bug because only one of the two requests should succeed. This race condition happens when
the two concurrently executing `UpdateAccount` methods both read the first `Version` of the row,
independently think their account `Version` is greater than what is currently stored in the database
and update the entry.

In fact, the problem is worse than that. Consider the following test that first updates the accounts
concurrently using two different versions, `2` and `3`, and then getting the account and asserting
that the account version should always be the latest, which is `3`.

```csharp
[Microsoft.Coyote.SystematicTesting.Test]
public static async Task TestGetAccountAfterConcurrentUpdate()
{
  // Initialize the mock in-memory DB and account manager.
  var dbCollection = new InMemoryDbCollection();
  var accountManager = new AccountManager(dbCollection);

  string accountName = "MyAccount";

  // Create the account, it should complete successfully and return true.
  var result = await accountManager.CreateAccount(accountName, "first_version", 1);
  Assert.True(result);

  // Call UpdateAccount twice without awaiting, which makes both methods run
  // asynchronously with each other.
  var task1 = accountManager.UpdateAccount(accountName, "second_version", 2);
  var task2 = accountManager.UpdateAccount(accountName, "third_version", 3);

  // Then wait both requests to complete.
  await Task.WhenAll(task1, task2);

  // Finally, get the account and assert that the version is always 3,
  // which is the latest updated version.
  var account = await accountManager.GetAccount(accountName);
  Assert.True(account.Version == 3);
}
```

Build the code, rewrite the assembly and run the test using Coyote for `10` iterations:

```plain
coyote rewrite .\AccountManager.ETags.dll
coyote test .\AccountManager.ETags.dll -m TestGetAccountAfterConcurrentUpdate -i 10
```

This test will fail in some iterations with account version `2` overwriting version `3`:

```plain
. Testing .\AccountManager.ETags.dll
... Method TestGetAccountAfterConcurrentUpdate
... Started the testing task scheduler (process:36036).
... Created '1' testing task (process:36036).
... Task 0 is using 'random' strategy (seed:2759716551).
..... Iteration #1
..... Iteration #2
..... Iteration #3
..... Iteration #4
... Task 0 found a bug.
... Emitting task 0 traces:
..... Writing AccountManager.ETags.dll\CoyoteOutput\AccountManager.ETags_0_0.txt
..... Writing AccountManager.ETags.dll\CoyoteOutput\AccountManager.ETags_0_0.schedule
... Elapsed 0.1317799 sec.
... Testing statistics:
..... Found 1 bug.
... Scheduling statistics:
..... Explored 4 schedules: 4 fair and 0 unfair.
..... Found 25.00% buggy schedules.
..... Number of scheduling points in fair terminating schedules: 20 (min), 26 (avg), 37 (max).
... Elapsed 0.2468594 sec.
```

You can see that this is not just a benign failure! The code doesn't respect the `UpdateAccount`
semantics in the presence of concurrency, which is a serious issue.

A database system like [Cosmos DB](https://azure.microsoft.com/services/cosmos-db/) provides
[ETags](https://en.wikipedia.org/wiki/HTTP_ETag) which you can use to only update the row if the
ETags match. This ensures that `UpdateAccount` will fail if another (concurrent) request updates the
row after `UpdateAccount` has read it, which indicates that `UpdateAccount` operated on stale data.

Let's take a look at a correct implementation of `UpdateAccount` that uses ETags.

```csharp
// Returns true if the account is updated, else false.
public async Task<bool> UpdateAccount(string accountName, string accountPayload, int accountVersion)
{
  Account existingAccount;
  Guid existingAccountETag;

  // Naive retry if ETags mismatch. In reality, you would use a proper retry policy.
  while (true)
  {
    try
    {
      (string value, Guid etag) = await this.AccountCollection.GetRow(accountName);
      existingAccount = JsonSerializer.Deserialize<Account>(value);
      existingAccountETag = etag;
    }
    catch (RowNotFoundException)
    {
      return false;
    }

    if (accountVersion <= existingAccount.Version)
    {
      return false;
    }

    var updatedAccount = new Account()
    {
      Name = accountName,
      Payload = accountPayload,
      Version = accountVersion
    };

    try
    {
      return await this.AccountCollection.UpdateRow(accountName,
        JsonSerializer.Serialize(updatedAccount), existingAccountETag);
    }
    catch (MismatchedETagException)
    {
      continue;
    }
    catch (RowNotFoundException)
    {
      return false;
    }
  }
}
```

You will also need to update the `IDbCollection` interface and `InMemoryDbCollection` mock to
support ETags, as well as create an `AccountEntity` type that is stored in the database and contains
the serialized `Account` as well as the corresponding `ETag`.

```csharp
public class AccountEntity
{
    public string Account { get; set; }

    public Guid ETag { get; set; }
}

public interface IDbCollection
{
  Task<bool> CreateRow(string key, string value);

  Task<bool> DoesRowExist(string key);

  Task<(string value, Guid etag)> GetRow(string key);

  Task<bool> UpdateRow(string key, string value, Guid etag);

  Task<bool> DeleteRow(string key);
}

public class InMemoryDbCollection : IDbCollection
{
  private readonly ConcurrentDictionary<string, AccountEntity> Collection;

  public InMemoryDbCollection()
  {
    this.Collection = new ConcurrentDictionary<string, AccountEntity>();
  }

  public Task<bool> CreateRow(string key, string value)
  {
    return Task.Run(() =>
    {
      var entity = new AccountEntity()
      {
        Account = value,
        ETag = Guid.NewGuid()
      };

      bool success = this.Collection.TryAdd(key, entity);
      if (!success)
      {
        throw new RowAlreadyExistsException();
      }

      return true;
    });
  }

  public Task<bool> DoesRowExist(string key)
  {
    return Task.Run(() =>
    {
      return this.Collection.ContainsKey(key);
    });
  }

  public Task<(string value, Guid etag)> GetRow(string key)
  {
    return Task.Run(() =>
    {
      bool success = this.Collection.TryGetValue(key, out AccountEntity entity);
      if (!success)
      {
        throw new RowNotFoundException();
      }

      return (entity.Account, entity.ETag);
    });
  }

  public Task<bool> UpdateRow(string key, string value, Guid etag)
  {
    return Task.Run(() =>
    {
      lock (this.Collection)
      {
        bool success = this.Collection.TryGetValue(key, out AccountEntity existingEntity);
        if (!success)
        {
          throw new RowNotFoundException();
        }
        else if (success && etag != existingEntity.ETag)
        {
          throw new MismatchedETagException();
        }

        var entity = new AccountEntity()
        {
          Account = value,
          ETag = Guid.NewGuid()
        };

        this.Collection[key] = entity;
        return true;
      }
    });
  }

  public Task<bool> DeleteRow(string key)
  {
    return Task.Run(() =>
    {
      bool success = this.Collection.TryRemove(key, out AccountEntity _);
      if (!success)
      {
        throw new RowNotFoundException();
      }

      return true;
    });
  }
}
```

The reason you need to acquire a `lock` in the `UpateRow` method is to ensure that no other task
races while the ETag is checked for mismatch. You don't need a `lock` in operations that don't check
the ETag as you are using a thread-safe concurrency dictionary.

Build the code one last time, rewrite the assembly and run the test using Coyote for `10` iterations:

```plain
coyote rewrite .\AccountManager.ETags.dll
coyote test .\AccountManager.ETags.dll -m TestGetAccountAfterConcurrentUpdate -i 10
```

Awesome, this time the test succeeds! If you try to remove the ETag check, it will fail as expected.

One interesting observation is that you used a lock inside the `InMemoryDbCollection` mock but not
inside the `AccountManager` code. The reason behind this choice is that in production
`AccountManager` can run across different processes or machines, so intra-process locks would not
work in that setting. With Coyote, however, you run the entire concurrency unit test in a single
process, so it is perfectly fine for the mock itself to take a lock, which makes it easier to
simulate the ETag functionality.

As you can see, it didn't take much effort to simulate ETags in the mock, as you just simulated the
semantics _in-memory_. This is significantly easier than if you had to implement the _real_ ETags
functionality in a production distributed system, where you would have to worry about arbitrary
failures.

## Get the sample source code

To get the complete source code for the `AccountManager.ETags` tutorial, clone the
[Coyote Samples git repo](http://github.com/microsoft/coyote-samples).

You can build the sample by running the following command:

```plain
powershell -f build.ps1
```
