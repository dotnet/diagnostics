// Import the pipeline declaration classes.
import org.dotnet.ci.pipelines.Pipeline

// The input project name (e.g. dotnet/diagnostics)
def project = GithubProject

// The input branch name (e.g. master)
def branch = GithubBranchName

// Possible OS's
//
// 'Windows_NT'
// 'Ubuntu'
// 'Ubuntu16.04'
// 'Ubuntu16.10'
// 'Debian8.4'
// 'RHEL7.2'
// 'Fedora24'
// 'CentOS7.1'
// 'OSX10.12'

// Possible Architechures
//
// 'arm', 
// 'arm64'
// 'x86'
// 'x64'

def configurations = [
    ['OS':'Windows_NT', 'Architechure':'x64', 'Configuration':'Release'],
    ['OS':'Ubuntu16.04', 'Architechure':'x64', 'Configuration':'Release'],
    ['OS':'CentOS7.1', 'Architechure':'x64', 'Configuration':'Release'],
]

// Create build and test pipeline job
def pipeline = Pipeline.createPipelineForGithub(this, project, branch, 'pipeline.groovy')

configurations.each { configParams ->
    def triggerName = "${configParams.OS} ${configParams.Architechure} ${configParams.Configuration} Build and Test"

    // Add PR trigger
    pipeline.triggerPipelineOnEveryGithubPR(triggerName, configParams)

    // Add trigger to run on merge
    pipeline.triggerPipelineOnGithubPush(configParams)
}
