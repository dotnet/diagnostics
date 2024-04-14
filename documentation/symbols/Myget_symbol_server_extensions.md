# MyGet symbol server extensions #

This is a feature request for MyGet to create an implementation of the [Zip Package Based Symbol Server](Package_Based_Symbol_Server.md). As far as I can tell MyGet already exposes the [Simple Symbol Query Protocol](Simple_Symbol_Query_Protocol.md), albeit never specified with that name and marketed only as being SymSrv-compatible.

## Endpoints ##

Myget would expose the same per-feed symbol server endpoints it does today, for example:

    https://www.myget.org/F/dev-feed/symbols
    https://www.myget.org/F/dev-feed/auth/11111111-1111-1111-1111-11111111111/symbols
    
If possible, it would also be nice to define an aggregate feed that can serve up any file from a set of feeds. We could then configure a larger organizational aggregate feed for customers to use. This probably needs some further discussion once Maarten is back from vacation.

## Packages ##

The service operates over all the packages that are active on that feed at a given time. Active packages are the packages that have been uploaded and not yet deleted either directly by the developer or implicitly by the configurable myget retention policies.

## Other clientKey sources ##

This feature doesn't preclude MyGet from continuing to satisfy requests using clientKeys there were automatically derived from package contents or any other source, but those keys should never take precedence to a key mapping provided explicitly by the client in symbol\_index.json.

## Access Privileges ##

The symbol service is expected to be equally accessible as reading the underlying feed by default. If the underlying feed requires authentication, so to should the symbol service end-point. We aren't requesting MyGet add any additional configurability, but its fine if they did.

## Package Management ##

We believe the existing mechanisms MyGet uses to upload and manage packages on a feed are sufficient to implicitly manage the content on the symbol service. No additional requests here.

## Performance ##

We don't yet know what loads to expect. Although the load will probably be far lower to start, here an initial guess at a load this service might need to scale to:

-  100,000 packages per-feed
-  100 million aggregate clientKeys per-feed
-  1000 requests/sec (burst)
-  1 GB/s (burst)
-  1 million requests/day (sustained load)
-  1 TB/day (sustained load)
-  Average response time: < 1 sec, 99% response time < 5 sec (measured from the time the request arrives at the myget server to the time file data begins streaming back)

My hope is that this is still well within the scalability range of other aspects of the existing myget service and thus doesn't require any significant investment in new infrastructure or more complex service logic.