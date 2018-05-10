// Import the pipeline declaration classes.
import org.dotnet.ci.pipelines.Pipeline

// The input project name (e.g. dotnet/diagnostics)
def project = GithubProject

// The input branch name (e.g. master)
def branch = GithubBranchName

// Create build and test pipeline job
def pipeline = Pipeline.createPipelineForGithub(this, project, branch, 'pipeline.groovy')

// Add PR trigger
pipeline.triggerPipelineOnEveryGithubPR('Build and Test')

// Add trigger to run on merge
pipeline.triggerPipelineOnGithubPush()
