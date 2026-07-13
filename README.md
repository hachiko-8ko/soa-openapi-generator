# SOA OpenAPI Generator

Imagine that you have an SOA web api. That's _string oriented architecture_. No types, no classes, the API just pulls strings out of the request content and returns strings as the result content.

Let's talk Swagger (now called OpenAPI). Swashbuckle, while an amazing library, a solid friend over years of writing ASP.NET web APIs, is going to break down and cry. But just because Swashbuckle can't autogenerate input/output contracts without knowing about the types doesn't mean you shouldn't get to enjoy the excellent swagger UI, so we at the Slop Company would like to share it with you now in a project we call _SOA OpenAPI Generator_.

## Configuration

See appsettings.json, your standard .net core configuration file. It should be straightforard.

## Preparation

To set this up, create a folder structure that mirrors your api. Just pretend it's back in the days when you had to drop your HTML files into the relevant physical directory based on the path. So, for example, if you have these endpoints:

- POST /customer/add
- POST /customer/update
- GET /customer/{id}/detail
- POST /customer/{id}/order

Create folders like so:

- /customer
- /customer/{id}

Then create json files inside them. Files can end with these extensions:

- .input.json (schema for an request, required)
- .output.json (schema for the 200 response, optional) (currently not handling other response codes)
- .config.json (optional additional information, see below for schema)

POST is almost always default in an app that wants a single string object rather than separated input params, so most of the time you can just create files named after the last segment in the URI, like "lastpart.input.json" but these URLs are also valid: "GET lastpart.input.json" and "POST lastpart.input.json" etc, all five HTTP verbs. Casing on the verb doesn't matter.

For our detailed example, inside /customer, create these json files, each containing an example of the json body:

- add.input.json
- add.output.json
- add.config.json
- update.input.json
- update.output.json
- update.config.json

And inside /customer/{id}, create:

- order.input.json
- order.output.json
- GET detail.input.json (Empty file)
- GET detail.output.json

This would generate the three endpoints mentioned.

## Config schema

This is defined in EndpointConfiguration.cs. These are the supported properties:

- summary
- description
- queryParams

Query Param definitions:

- name
- type
- description
- required

If the path contains a field surrounded by {} like {id} a parameter is automatically created. It is always required by specification, but you can use the config to define the description.

```
{
  "summary": "Add or update a user address",
  "description": "Users have one or more address. To update a user's address, send id to a positive integer. To add a new address, set it to 0, and the return object will include the new address id.",
  "queryParams": [
     {
      "name": "id",
      "type": "number",
      "description": "Set the id (key) of the address to add or update.",
      "required": true
    },
    {
      "name": "dryRun",
      "type": "boolean",
      "description": "If true, validates the payload without committing changes."
    }
  ]
}
```

## Execution

It's a console application that takes no input parameters. Just update the config file and run the EXE (on windows) or "dotnet soa-openapi-generator.dll" (cross-platform).

Windows build should be self-contained and cross-platform build is a smaller package that depends on dotnet being installed.

## Limitations

Right now, this is in an early version. There are no guarantees that we're going to add new features. Or not. Only one person (watashi) has ever used any of this personal code, so feature sets tend to be either very limited or excessive (a bit too much "wouldn't it be neat if" going on here TBH).

But working with broken AI is so frustrating that as soon as I have something stable, I'm going to back away slowly while making soothing sounds while looking around for a rock. And installing .NET ate up all the space I'd cleared over the last few weeks, so I have the urge to delete it and free up a critical GB of spinning rust.

So here are the current limits:

- Always need an .input file, but it can be empty (I might change this next)
- No empty endpoint names (e.g. CustomerController with a default OnPost method mapping to /customer)

## The Slop Company

The Slop Company is a band of AI agents building applications. Everything released by Slop Company is vibe coded using Generative AI ... not because it's necessary or efficient or a good idea, but because it's there. 100% of all slop code is reviewed and edited and refactored by me.

_Aside from basic web apis, which seem to be AI's strong suit, it's rare to see AI make it to the end of a project without handholding._
